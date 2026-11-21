namespace NeuralRigStudio.Audio;

public sealed record TunerSnapshot(
    bool Active,
    string NoteName,
    int Octave,
    float Cents,
    float FreqHz,
    float Clarity = 0f,
    float LevelDb = -96f);

public sealed record VisualizerSnapshot(
    bool Running,
    bool HasSignal,
    bool HasIrCurve,
    float PeakDb,
    float[] Bands,
    float[] PeakCaps,
    float[] Wave,
    float[] IrCurve,
    TunerSnapshot Tuner);

/// <summary>
/// 제로 할당 Radix-2 1024-pt FFT + 36밴드 로그 스펙트럼 + 피크 홀드 + 제로크로싱 파형 + CabIr 응답 + MPM 크로매틱 튜너.
/// 50fps 텔레메트리 프레임을 WebView2 HTML5 Canvas로 전송한다.
/// </summary>
public sealed class VisualizerAnalyzer
{
    public const int FftN = 1024;
    public const int TunerN = 4096;
    public const int BandCount = 36;
    public const int WavePtsCount = 180;

    private static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private readonly float[] _raw = new float[FftN];
    private readonly float[] _dryRaw = new float[TunerN];
    private readonly GuitarPitchDetector _pitchDetector = new();
    private readonly float[] _yinDiff = new float[1024];
    private readonly float[] _yinCmndf = new float[1024];

    private int _tunerRefA4Bits = BitConverter.SingleToInt32Bits(440f);
    public float TunerRefA4Hz
    {
        get => BitConverter.Int32BitsToSingle(Volatile.Read(ref _tunerRefA4Bits));
        set => Volatile.Write(ref _tunerRefA4Bits, BitConverter.SingleToInt32Bits(Math.Clamp(value, 420f, 460f)));
    }

    private readonly float[] _fftRe = new float[FftN];
    private readonly float[] _fftIm = new float[FftN];
    private readonly float[] _hann = new float[FftN];
    private readonly int[] _bandBinLo = new int[BandCount];
    private readonly int[] _bandBinHi = new int[BandCount];

    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _smoothBands = new float[BandCount];
    private readonly float[] _peakCaps = new float[BandCount];
    private readonly int[] _peakHold = new int[BandCount];
    private readonly float[] _wave = new float[WavePtsCount];
    private readonly float[] _irCurve = new float[BandCount];

    private int _sampleRate = 44100;
    private bool _hasIrCurve;
    private float _smoothedFreq;
    private int _tunerHoldTicks;

    public VisualizerAnalyzer()
    {
        for (int i = 0; i < FftN; i++)
            _hann[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftN - 1)));
        BuildLogBands(44100);
    }

    private void BuildLogBands(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        const float fMin = 65f;
        float fMax = Math.Min(11500f, _sampleRate * 0.45f);
        float binHz = (float)_sampleRate / FftN;

        for (int b = 0; b < BandCount; b++)
        {
            float t0 = (float)b / BandCount;
            float t1 = (float)(b + 1) / BandCount;
            float f0 = fMin * MathF.Pow(fMax / fMin, t0);
            float f1 = fMin * MathF.Pow(fMax / fMin, t1);
            int lo = Math.Clamp((int)MathF.Floor(f0 / binHz), 1, FftN / 2 - 1);
            int hi = Math.Clamp((int)MathF.Ceiling(f1 / binHz), lo + 1, FftN / 2);
            _bandBinLo[b] = lo;
            _bandBinHi[b] = hi;
        }
    }

    public void SetToneProfile(ToneProfile? profile) =>
        SetCabIr(profile?.CabIr, profile?.SampleRate ?? _sampleRate);

    public void SetCabIr(float[]? cabIr, int sampleRate = 44100)
    {
        if (cabIr == null || cabIr.Length == 0)
        {
            _hasIrCurve = false;
            Array.Clear(_irCurve);
            return;
        }

        if (sampleRate > 0 && sampleRate != _sampleRate)
            BuildLogBands(sampleRate);

        Array.Clear(_fftRe);
        Array.Clear(_fftIm);
        int copyN = Math.Min(cabIr.Length, FftN);
        Array.Copy(cabIr, _fftRe, copyN);
        FftInPlace(_fftRe, _fftIm);

        float maxDb = -120f;
        Span<float> bandDb = stackalloc float[BandCount];
        for (int b = 0; b < BandCount; b++)
        {
            float sum = 0f;
            int cnt = 0;
            for (int k = _bandBinLo[b]; k < _bandBinHi[b]; k++)
            {
                float m = MathF.Sqrt(_fftRe[k] * _fftRe[k] + _fftIm[k] * _fftIm[k]);
                sum += m;
                cnt++;
            }
            float avg = cnt > 0 ? sum / cnt : 1e-6f;
            float db = 20f * MathF.Log10(MathF.Max(1e-6f, avg));
            bandDb[b] = db;
            if (db > maxDb) maxDb = db;
        }

        for (int b = 0; b < BandCount; b++)
        {
            float rel = Math.Clamp((bandDb[b] - (maxDb - 30f)) / 30f, 0f, 1f);
            _irCurve[b] = MathF.Round(0.08f + rel * 0.80f, 3);
        }
        _hasIrCurve = true;
    }

    public VisualizerSnapshot UpdateFromRouter(AudioRouter router)
    {
        bool active = router.IsRunning;
        bool hasSignal = false;
        float peakDb = -90f;
        TunerSnapshot tuner = new(false, "--", 0, 0f, 0f);

        if (active)
        {
            int sr = router.EffectiveSampleRate;
            if (sr > 0 && sr != _sampleRate)
                BuildLogBands(sr);

            router.CopyRecentSamples(_raw);
            router.CopyRecentDrySamples(_dryRaw);

            float pk = 0f;
            for (int i = 0; i < FftN; i++)
            {
                float a = MathF.Abs(_raw[i]);
                if (a > pk) pk = a;
            }
            hasSignal = pk > 0.0008f;
            peakDb = pk > 1e-5f ? 20f * MathF.Log10(pk) : -90f;

            tuner = DetectPitchMpm(_dryRaw, _sampleRate);

            // 제로크로싱 트리거 안정화
            int trig = 0;
            for (int i = 1; i < FftN / 2; i++)
            {
                if (_raw[i - 1] <= 0f && _raw[i] > 0f)
                {
                    trig = i;
                    break;
                }
            }
            int span = Math.Min(512, FftN - trig);
            for (int i = 0; i < WavePtsCount; i++)
            {
                int idx = trig + (i * (span - 1)) / (WavePtsCount - 1);
                _wave[i] = MathF.Round(Math.Clamp(_raw[idx], -1f, 1f), 3);
            }

            for (int i = 0; i < FftN; i++)
            {
                _fftRe[i] = _raw[i] * _hann[i];
                _fftIm[i] = 0f;
            }
            FftInPlace(_fftRe, _fftIm);

            const float normScale = 2f / FftN;
            for (int b = 0; b < BandCount; b++)
            {
                float maxMag = 0f;
                for (int k = _bandBinLo[b]; k < _bandBinHi[b]; k++)
                {
                    float mag = MathF.Sqrt(_fftRe[k] * _fftRe[k] + _fftIm[k] * _fftIm[k]) * normScale;
                    if (mag > maxMag) maxMag = mag;
                }
                float db = 20f * MathF.Log10(MathF.Max(1e-5f, maxMag)) + b * 0.16f;
                _bands[b] = Math.Clamp((db + 68f) / 66f, 0f, 1f);
            }
        }
        else
        {
            Array.Clear(_bands);
            for (int i = 0; i < WavePtsCount; i++) _wave[i] = MathF.Round(_wave[i] * 0.72f, 3);
            _smoothedFreq = 0f;
            _tunerHoldTicks = 0;
            _pitchDetector.Reset();
        }

        for (int b = 0; b < BandCount; b++)
        {
            float target = _bands[b];
            float cur = _smoothBands[b];
            _smoothBands[b] = target > cur
                ? cur * 0.30f + target * 0.70f
                : cur * 0.80f + target * 0.20f;
            if (_smoothBands[b] < 0.002f) _smoothBands[b] = 0f;
            _smoothBands[b] = MathF.Round(_smoothBands[b], 3);

            if (_smoothBands[b] >= _peakCaps[b])
            {
                _peakCaps[b] = _smoothBands[b];
                _peakHold[b] = 14;
            }
            else if (_peakHold[b] > 0)
            {
                _peakHold[b]--;
            }
            else
            {
                _peakCaps[b] = MathF.Round(MathF.Max(0f, _peakCaps[b] - 0.022f), 3);
            }
        }

        return new VisualizerSnapshot(
            active,
            hasSignal,
            _hasIrCurve,
            MathF.Round(peakDb, 1),
            (float[])_smoothBands.Clone(),
            (float[])_peakCaps.Clone(),
            (float[])_wave.Clone(),
            _hasIrCurve ? (float[])_irCurve.Clone() : Array.Empty<float>(),
            tuner);
    }

    /// <summary>MPM/NSDF 기반 크로매틱 튜너 검출 (4096샘플, 약한 기음 보정, 기준음 A4 가변).</summary>
    public TunerSnapshot DetectPitchMpm(ReadOnlySpan<float> buf, int sampleRate)
    {
        var p = _pitchDetector.Detect(buf, sampleRate, TunerRefA4Hz, 62f);
        return new TunerSnapshot(p.Active, p.NoteName, p.Octave, p.Cents, p.FreqHz, p.Clarity, p.LevelDb);
    }

    /// <summary>드라이 기타 신호 대상 제로 할당 YIN 피치 검출기 (65Hz~1350Hz, 패러볼릭 보간 ±0.5센트급 정밀도).</summary>
    public TunerSnapshot DetectPitchYin(ReadOnlySpan<float> buf, int sampleRate)
    {
        const int half = 1024;
        if (buf.Length < half * 2 || sampleRate < 8000)
            return new TunerSnapshot(false, "--", 0, 0f, 0f);

        double sum = 0.0;
        for (int i = 0; i < half; i++)
            sum += buf[i];
        float mean = (float)(sum / half);

        double sumSq = 0.0;
        for (int i = 0; i < half; i++)
        {
            float ac = buf[i] - mean;
            sumSq += ac * ac;
        }
        float rms = MathF.Sqrt((float)(sumSq / half));
        if (rms < 0.00045f)
        {
            if (_tunerHoldTicks > 0)
            {
                _tunerHoldTicks--;
                return FormatNote(_smoothedFreq, active: false);
            }
            _smoothedFreq = 0f;
            return new TunerSnapshot(false, "--", 0, 0f, 0f);
        }

        int tauMin = Math.Clamp(sampleRate / 1350, 12, half - 4);
        int tauMax = Math.Clamp(sampleRate / 65, tauMin + 8, half - 2);

        _yinCmndf[0] = 1f;
        float runningSum = 0f;
        int vecN = System.Numerics.Vector<float>.Count;
        bool useVec = System.Numerics.Vector.IsHardwareAccelerated && half >= vecN;

        for (int tau = 1; tau <= tauMax; tau++)
        {
            float d = 0f;
            int j = 0;
            if (useVec)
            {
                var acc = System.Numerics.Vector<float>.Zero;
                for (; j <= half - vecN; j += vecN)
                {
                    var va = new System.Numerics.Vector<float>(buf.Slice(j, vecN));
                    var vb = new System.Numerics.Vector<float>(buf.Slice(j + tau, vecN));
                    var vd = va - vb;
                    acc += vd * vd;
                }
                d = System.Numerics.Vector.Dot(acc, System.Numerics.Vector<float>.One);
            }
            for (; j < half; j++)
            {
                float diff = buf[j] - buf[j + tau];
                d += diff * diff;
            }
            _yinDiff[tau] = d;
            runningSum += d;
            _yinCmndf[tau] = runningSum > 1e-9f ? d * tau / runningSum : 1f;
        }

        int bestTau = -1;
        const float yinThresh = 0.16f;
        for (int tau = tauMin; tau <= tauMax; tau++)
        {
            if (_yinCmndf[tau] < yinThresh)
            {
                while (tau + 1 <= tauMax && _yinCmndf[tau + 1] < _yinCmndf[tau])
                    tau++;
                bestTau = tau;
                break;
            }
        }

        if (bestTau < 0)
        {
            float minVal = 1f;
            for (int tau = tauMin; tau <= tauMax; tau++)
            {
                if (_yinCmndf[tau] < minVal)
                {
                    minVal = _yinCmndf[tau];
                    bestTau = tau;
                }
            }
            if (minVal > 0.36f || bestTau < 0)
            {
                if (_tunerHoldTicks > 0)
                {
                    _tunerHoldTicks--;
                    return FormatNote(_smoothedFreq, active: true);
                }
                return new TunerSnapshot(false, "--", 0, 0f, 0f);
            }
        }

        // 2차 다항식 보간으로 서브샘플 정밀 주기 산출 (대칭 오차함수 _yinDiff 사용)
        float betterTau = bestTau;
        if (bestTau > tauMin && bestTau < tauMax)
        {
            float s0 = _yinDiff[bestTau - 1];
            float s1 = _yinDiff[bestTau];
            float s2 = _yinDiff[bestTau + 1];
            float denom = s0 + s2 - 2f * s1;
            if (denom > 1e-9f)
            {
                float delta = 0.5f * (s0 - s2) / denom;
                betterTau += Math.Clamp(delta, -1f, 1f);
            }
        }

        float freq = sampleRate / MathF.Max(1f, betterTau);
        if (_smoothedFreq <= 0f || MathF.Abs(MathF.Log2(freq / _smoothedFreq)) > 0.08f)
            _smoothedFreq = freq;
        else
            _smoothedFreq = _smoothedFreq * 0.55f + freq * 0.45f;

        _tunerHoldTicks = 15;
        return FormatNote(_smoothedFreq, active: true);
    }

    private static TunerSnapshot FormatNote(float freq, bool active)
    {
        if (freq <= 20f || !float.IsFinite(freq))
            return new TunerSnapshot(false, "--", 0, 0f, 0f);

        double midiFloat = 69.0 + 12.0 * Math.Log2(freq / 440.0);
        int midi = (int)Math.Round(midiFloat);
        float cents = MathF.Round((float)((midiFloat - midi) * 100.0), 1);
        int noteIdx = ((midi % 12) + 12) % 12;
        int octave = midi / 12 - 1;
        return new TunerSnapshot(active, NoteNames[noteIdx], octave, cents, MathF.Round(freq, 1));
    }

    public static void FftInPlace(float[] re, float[] im)
    {
        int n = re.Length;
        int j = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * MathF.PI / len;
            float wLenRe = MathF.Cos(ang);
            float wLenIm = MathF.Sin(ang);
            int half = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                float wRe = 1f, wIm = 0f;
                for (int k = 0; k < half; k++)
                {
                    int idx1 = i + k;
                    int idx2 = idx1 + half;
                    float uRe = re[idx1], uIm = im[idx1];
                    float tRe = re[idx2] * wRe - im[idx2] * wIm;
                    float tIm = re[idx2] * wIm + im[idx2] * wRe;
                    re[idx1] = uRe + tRe;
                    im[idx1] = uIm + tIm;
                    re[idx2] = uRe - tRe;
                    im[idx2] = uIm - tIm;

                    float nextWRe = wRe * wLenRe - wIm * wLenIm;
                    wIm = wRe * wLenIm + wIm * wLenRe;
                    wRe = nextWRe;
                }
            }
        }
    }
}

