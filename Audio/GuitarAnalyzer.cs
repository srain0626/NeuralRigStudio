namespace NeuralRigStudio.Audio;

/// <summary>
/// UI 스레드 전용 6단계 가이드 기타 DI 프로파일러.
/// RT 오디오 콜백 외부에서 AudioRouter 드라이 링 버퍼를 소비하므로 RT 할당은 0 B다.
/// </summary>
public sealed class GuitarAnalyzer
{
    private const int FftN = 4096;
    private const int CombFftN = 8192;
    private const float InvNormPow = 2f / (0.375f * FftN * FftN);
    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly float[] CombWeights = { 1.0f, 1.0f, 0.85f, 0.65f, 0.45f };
    private static readonly int[] CompetingSemitones = { -7, -6, -5, -4, -3, -2, 2, 3, 4, 5, 6, 7 };

    private enum StepKind { Silence, Note, Strum }

    private sealed record StepSpec(
        string Id,
        StepKind Kind,
        string Title,
        string TargetNote,
        float TargetFreqHz,
        float HoldSec,
        string Instruction);

    private static readonly StepSpec[] Steps =
    {
        new("silence", StepKind.Silence, "무음 노이즈 & 60Hz 험 측정", "MUTE", 0f, 1.10f,
            "기타 볼륨 노브를 최대로 올리고 손으로 줄을 가볍게 뮤트한 채 1.1초간 대기하세요."),
        new("note_e2", StepKind.Note, "6번줄 개방현 E2", "E2", 82.41f, 0.85f,
            "6번줄 개방현 E2 (82.4 Hz)를 평소 피킹 세기로 1회 튕기고 울림을 유지하세요."),
        new("note_d3", StepKind.Note, "4번줄 개방현 D3", "D3", 146.83f, 0.85f,
            "4번줄 개방현 D3 (146.8 Hz)를 1회 튕기고 울림을 유지하세요."),
        new("note_b3", StepKind.Note, "2번줄 개방현 B3", "B3", 246.94f, 0.85f,
            "2번줄 개방현 B3 (246.9 Hz)를 1회 튕기고 울림을 유지하세요."),
        new("note_e4", StepKind.Note, "1번줄 개방현 E4", "E4", 329.63f, 0.85f,
            "1번줄 개방현 E4 (329.6 Hz)를 1회 튕기고 울림을 유지하세요."),
        new("strum", StepKind.Strum, "오픈 코드 강한 스트럼", "STRUM", 0f, 0.85f,
            "Em 코드 또는 6줄 개방현을 가장 강하게 스트럼하고 울림을 유지하세요."),
    };

    private readonly GuitarPitchDetector _detector = new();
    private readonly float[] _pitchWindow = new float[FftN];
    private readonly float[] _pitchClean = new float[FftN];
    private readonly float[] _combRe = new float[CombFftN];
    private readonly float[] _combIm = new float[CombFftN];
    private readonly float[] _combMag = new float[CombFftN / 2];
    private readonly float[] _combSort = new float[CombFftN / 2];

    private readonly float[] _stepBuf = new float[160000];
    private readonly float[] _fftRe = new float[FftN];
    private readonly float[] _fftIm = new float[FftN];
    private readonly float[] _hann = new float[FftN];
    private readonly double[] _fftPowerAccum = new double[FftN / 2];

    private string _guitarName = "내 기타";
    private bool _active;
    private bool _completed;
    private int _stepIndex;
    private int _sampleRate = 44100;
    private int _stepCount;
    private int _cooldownSamples;
    private bool _noteLocked;
    private int _lockConfirmSamples;
    private int _lockMissSamples;
    private int _dropSilenceSamples;
    private bool _strumTriggered;
    private int _silenceRetryWarnTicks;

    // 측정 결과 누적 상태
    private float _noiseFloorDb = -75f;
    private float _bandNoiseFloorDb = -78f;
    private float _hum60HzDb = -82f;
    private float _humProminenceDb = 0f;
    private bool _silenceMeasured;

    private float _strumPeakDb = -16f;
    private float _strumRmsDb = -26f;
    private float _strumCentroidHz = 1100f;
    private readonly double[] _strumBands = new double[5];
    private bool _strumMeasured;

    private readonly List<GuitarNoteMeasurementDto> _noteResults = new();
    private readonly List<double[]> _noteBandList = new();
    private double _matchedFreqSum;
    private int _matchedFreqCount;

    // 실시간 UI 피드백 상태
    private string _liveNote = "--";
    private float _liveFreqHz;
    private float _liveCents;
    private float _liveLevelDb = -90f;
    private float _recentPeakDb = -96f;
    private bool _isMatched;
    private string _statusHint = "";
    private GuitarProfileDto? _completedProfile;

    public GuitarAnalyzer()
    {
        for (int i = 0; i < FftN; i++)
            _hann[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftN - 1)));
    }

    public bool IsActive => _active;
    public GuitarProfileDto? CompletedProfile => _completedProfile?.Clone();

    public GuitarAnalysisLiveStateDto Start(string? guitarName)
    {
        _guitarName = string.IsNullOrWhiteSpace(guitarName) ? $"내 기타 {DateTime.Now:MMdd-HHmm}" : guitarName.Trim();
        _active = true;
        _completed = false;
        _stepIndex = 0;
        _stepCount = 0;
        _cooldownSamples = 0;
        _noteLocked = false;
        _lockConfirmSamples = 0;
        _lockMissSamples = 0;
        _dropSilenceSamples = 0;
        _strumTriggered = false;
        _silenceRetryWarnTicks = 0;

        _noiseFloorDb = -75f;
        _bandNoiseFloorDb = -78f;
        _hum60HzDb = -82f;
        _humProminenceDb = 0f;
        _silenceMeasured = false;
        _strumPeakDb = -16f;
        _strumRmsDb = -26f;
        _strumCentroidHz = 1100f;
        Array.Clear(_strumBands);
        _strumMeasured = false;
        _noteResults.Clear();
        _noteBandList.Clear();
        _matchedFreqSum = 0;
        _matchedFreqCount = 0;
        _completedProfile = null;

        Array.Clear(_pitchWindow);
        _detector.Reset();
        _liveNote = "--";
        _liveFreqHz = 0f;
        _liveCents = 0f;
        _liveLevelDb = -90f;
        _recentPeakDb = -96f;
        _isMatched = false;
        _statusHint = Steps[0].Instruction;
        return GetLiveState();
    }

    public void Cancel()
    {
        _active = false;
        _completed = false;
        _completedProfile = null;
        _statusHint = "";
    }

    public GuitarAnalysisLiveStateDto SkipOrForceStep()
    {
        if (!_active || _completed)
            return GetLiveState();

        var step = Steps[_stepIndex];
        int sr = Math.Max(8000, _sampleRate);
        bool hasEnough = _stepCount >= (int)(0.25f * sr);

        switch (step.Kind)
        {
            case StepKind.Silence:
                if (hasEnough)
                    FinalizeSilenceStep(_stepBuf.AsSpan(0, _stepCount), sr);
                else
                {
                    _noiseFloorDb = -74f;
                    _bandNoiseFloorDb = -78f;
                    _hum60HzDb = -82f;
                    _humProminenceDb = 0f;
                    _silenceMeasured = false;
                }
                break;

            case StepKind.Note:
                if (hasEnough && _matchedFreqCount > 0)
                    _noteResults.Add(AnalyzeNoteStep(step, _stepBuf.AsSpan(0, _stepCount), sr));
                else
                    _noteResults.Add(new GuitarNoteMeasurementDto
                    {
                        StepId = step.Id,
                        TargetLabel = step.Title,
                        TargetNote = step.TargetNote,
                        TargetFreqHz = step.TargetFreqHz,
                        Skipped = true,
                    });
                break;

            case StepKind.Strum:
                if (hasEnough)
                    FinalizeStrumStep(_stepBuf.AsSpan(0, _stepCount), sr);
                else
                    _strumMeasured = false;
                break;
        }

        AdvanceStep(sr);
        return GetLiveState();
    }

    public void FeedDrySamples(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (!_active || _completed || samples.IsEmpty)
            return;

        int sr = Math.Clamp(sampleRate, 8000, 192000);
        _sampleRate = sr;

        PushToPitchWindow(samples);
        float chunkBandRmsDb = PreparePitchClean(sr, Math.Clamp(samples.Length, 256, 2048), out float rawChunkPeakDb);
        _liveLevelDb = chunkBandRmsDb;
        float decayDb = 18f * (samples.Length / (float)sr);
        _recentPeakDb = MathF.Max(chunkBandRmsDb, MathF.Max(rawChunkPeakDb, _recentPeakDb - decayDb));

        var step = Steps[_stepIndex];
        AnalyzeLivePitch(sr, step.Kind == StepKind.Note ? step.TargetFreqHz : 0f, chunkBandRmsDb, rawChunkPeakDb,
            out bool pitchMatched, out float matchedFreq, out float matchedCents);

        // 단계 전환 직후 이전 줄 잔향 유입 방지 쿨다운
        if (_cooldownSamples > 0)
        {
            _cooldownSamples = Math.Max(0, _cooldownSamples - samples.Length);
            _isMatched = false;
            return;
        }

        int targetCount = (int)MathF.Round(step.HoldSec * sr);

        switch (step.Kind)
        {
            case StepKind.Silence:
                ProcessSilenceChunk(samples, sr, targetCount, chunkBandRmsDb, rawChunkPeakDb);
                break;

            case StepKind.Note:
                ProcessNoteChunk(step, samples, sr, targetCount, chunkBandRmsDb, rawChunkPeakDb,
                    pitchMatched, matchedFreq, matchedCents);
                break;

            case StepKind.Strum:
                ProcessStrumChunk(samples, sr, targetCount, chunkBandRmsDb, rawChunkPeakDb);
                break;
        }
    }

    private float PreparePitchClean(int sr, int tailLen, out float rawTailPeakDb)
    {
        // DC 오프셋 계산 및 제거
        double dc = 0;
        for (int i = 0; i < FftN; i++) dc += _pitchWindow[i];
        float mean = (float)(dc / FftN);

        int peakStart = Math.Max(0, FftN - tailLen);
        float maxAbs = 0f;
        for (int i = peakStart; i < FftN; i++)
        {
            float a = MathF.Abs(_pitchWindow[i] - mean);
            if (a > maxAbs) maxAbs = a;
        }
        rawTailPeakDb = SanitizeDb(20f * MathF.Log10(MathF.Max(1e-5f, maxAbs)), -96f, 0f);

        // 1차 55Hz 고역 통과 필터 (서브소닉/DC 차단)
        float rc = 1f / (2f * MathF.PI * 55f);
        float dt = 1f / sr;
        float alpha = rc / (rc + dt);
        float prevX = _pitchWindow[0] - mean;
        float prevY = 0f;
        _pitchClean[0] = 0f;
        for (int i = 1; i < FftN; i++)
        {
            float x = _pitchWindow[i] - mean;
            float y = alpha * (prevY + x - prevX);
            prevX = x;
            prevY = y;
            _pitchClean[i] = y;
        }

        // 60Hz & 120Hz 노치 필터 (험 감지 시 또는 피치 추출 시 항상 60Hz/120Hz 간섭 억제)
        if (!_silenceMeasured || _humProminenceDb >= 4f || _hum60HzDb > -74f)
        {
            ApplyNotchInPlace(_pitchClean, sr, 60f, 5.0f);
            ApplyNotchInPlace(_pitchClean, sr, 120f, 8.0f);
        }

        int tailCount = FftN - peakStart;
        double sumSq = 0;
        for (int i = peakStart; i < FftN; i++)
        {
            float s = _pitchClean[i];
            sumSq += (double)s * s;
        }
        float rms = MathF.Sqrt((float)(sumSq / Math.Max(1, tailCount)));
        return SanitizeDb(20f * MathF.Log10(MathF.Max(1e-5f, rms)), -96f, 0f);
    }

    private static void ApplyNotchInPlace(float[] buf, int sr, float f0, float q)
    {
        float w0 = 2f * MathF.PI * f0 / sr;
        float cosW0 = MathF.Cos(w0);
        float alpha = MathF.Sin(w0) / (2f * q);
        float a0Inv = 1f / (1f + alpha);
        float b0 = a0Inv;
        float b1 = -2f * cosW0 * a0Inv;
        float b2 = b0;
        float a1 = b1;
        float a2 = (1f - alpha) * a0Inv;

        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
        for (int i = 0; i < buf.Length; i++)
        {
            float x0 = buf[i];
            float y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x0;
            y2 = y1; y1 = y0;
            buf[i] = y0;
        }
    }

    private void AnalyzeLivePitch(
        int sr,
        float targetFreqHz,
        float chunkBandRmsDb,
        float rawChunkPeakDb,
        out bool pitchMatched,
        out float matchedFreq,
        out float matchedCents)
    {
        pitchMatched = false;
        matchedFreq = 0f;
        matchedCents = 0f;

        // 1) MPM/NSDF 공용 엔진으로 실시간 피치 추출
        var mpm = _detector.Detect(_pitchClean, sr, 440f, 62f);
        bool mpmValid = mpm.Active && mpm.FreqHz >= 55f && mpm.FreqHz <= 1350f;
        float mpmFreq = mpmValid ? mpm.FreqHz : 0f;

        // 2) Zero-Padded 8192-pt FFT 스펙트럼 (5.38 Hz/bin 해상도) + 미디언 플로어 + 하모닉 콤
        Array.Clear(_combRe);
        Array.Clear(_combIm);
        for (int i = 0; i < FftN; i++)
            _combRe[i] = _pitchClean[i] * _hann[i];
        VisualizerAnalyzer.FftInPlace(_combRe, _combIm);

        float binHz = (float)sr / CombFftN;
        int halfBins = CombFftN / 2;
        int kFloorLo = Math.Clamp((int)MathF.Ceiling(72f / binHz), 1, halfBins - 1);
        int kFloorHi = Math.Clamp((int)MathF.Floor(3600f / binHz), kFloorLo + 2, halfBins - 1);
        int sortLen = 0;
        for (int k = 1; k < halfBins; k++)
        {
            float m = MathF.Sqrt(_combRe[k] * _combRe[k] + _combIm[k] * _combIm[k]);
            _combMag[k] = m;
            if (k >= kFloorLo && k <= kFloorHi)
                _combSort[sortLen++] = m;
        }
        Array.Sort(_combSort, 0, sortLen);
        float medianFloorMag = sortLen > 0 ? _combSort[sortLen / 2] : 1e-6f;

        bool combMatched = false;
        float combFreq = 0f;
        bool hasCombEnergy = chunkBandRmsDb >= -70f || rawChunkPeakDb >= -60f;

        if (targetFreqHz > 20f && hasCombEnergy)
        {
            float bestTargetScore = -1f;
            float bestCand = targetFreqHz;
            float bestLowFrac = 0f;

            // ±115 센트 범위 내 탐색 (Eb 반음 내림 -100ct 포함, 인접 온음 ±200ct 간섭 배제)
            for (int ct = -115; ct <= 115; ct += 10)
            {
                float f = targetFreqHz * MathF.Pow(2f, ct / 1200f);
                float score = ScoreHarmonicComb(f, binHz, out float lowFrac);
                if (score > bestTargetScore)
                {
                    bestTargetScore = score;
                    bestCand = f;
                    bestLowFrac = lowFrac;
                }
            }
            for (int dCt = -8; dCt <= 8; dCt += 2)
            {
                float f = bestCand * MathF.Pow(2f, dCt / 1200f);
                float score = ScoreHarmonicComb(f, binHz, out float lowFrac);
                if (score > bestTargetScore)
                {
                    bestTargetScore = score;
                    bestCand = f;
                    bestLowFrac = lowFrac;
                }
            }

            float bestOtherScore = 0f;
            for (int i = 0; i < CompetingSemitones.Length; i++)
            {
                float fComp = targetFreqHz * MathF.Pow(2f, CompetingSemitones[i] / 12f);
                if (fComp < 60f) continue;
                float sComp = ScoreHarmonicComb(fComp, binHz, out _);
                if (sComp > bestOtherScore) bestOtherScore = sComp;
            }

            // 1옥타브 위(2*f0)와 구분하기 위한 홀수차 배음 확인 (E3 실제 연주 시 E2 오인 방지)
            float h1 = PeakHarmonicBin(bestCand, binHz);
            float h2 = PeakHarmonicBin(bestCand * 2f, binHz);
            float h3 = PeakHarmonicBin(bestCand * 3f, binHz);
            bool hasTargetIdentity = h1 + h3 >= 0.14f * MathF.Max(h2, 1e-6f);

            float snrVsMedian = bestTargetScore / (5f * medianFloorMag + 1e-6f);
            if (bestLowFrac >= 0.16f && hasTargetIdentity && snrVsMedian >= 3.0f && bestTargetScore >= bestOtherScore * 1.15f)
            {
                combMatched = true;
                combFreq = bestCand;
            }
        }

        // 3) MPM 목표 음정 대조 (기본 주파수 + 2배음/3배음 서브옥타브 보정)
        bool mpmMatched = false;
        bool mpmContradicts = false;
        float mpmMatchedFreq = mpmFreq;

        if (targetFreqHz > 20f && mpmValid)
        {
            float c1 = 1200f * MathF.Log2(mpmFreq / targetFreqHz);
            float c2 = 1200f * MathF.Log2((mpmFreq * 0.5f) / targetFreqHz);
            float c3 = 1200f * MathF.Log2((mpmFreq / 3f) / targetFreqHz);

            if (MathF.Abs(c1) <= 115f)
            {
                mpmMatched = true;
                mpmMatchedFreq = mpmFreq;
            }
            else if (MathF.Abs(c2) <= 115f)
            {
                float fHalf = mpmFreq * 0.5f;
                float sHalf = ScoreHarmonicComb(fHalf, binHz, out float lowFracHalf);
                float h1 = PeakHarmonicBin(fHalf, binHz);
                float h2 = PeakHarmonicBin(mpmFreq, binHz);
                float h3 = PeakHarmonicBin(fHalf * 3f, binHz);
                if (sHalf > 0f && lowFracHalf >= 0.14f && (h1 + h3) >= 0.14f * MathF.Max(h2, 1e-6f))
                {
                    mpmMatched = true;
                    mpmMatchedFreq = fHalf;
                }
            }
            else if (targetFreqHz < 160f && MathF.Abs(c3) <= 115f)
            {
                float fThird = mpmFreq / 3f;
                float h1 = PeakHarmonicBin(fThird, binHz);
                float h2 = PeakHarmonicBin(fThird * 2f, binHz);
                float h3 = PeakHarmonicBin(mpmFreq, binHz);
                if ((h1 + h2) >= 0.12f * MathF.Max(h3, 1e-6f))
                {
                    mpmMatched = true;
                    mpmMatchedFreq = fThird;
                }
            }
            else
            {
                mpmContradicts = true;
            }
        }

        if (targetFreqHz > 20f)
        {
            if (mpmMatched)
            {
                pitchMatched = true;
                matchedFreq = mpmMatchedFreq;
            }
            else if (combMatched && !mpmContradicts)
            {
                pitchMatched = true;
                matchedFreq = combFreq;
            }

            if (pitchMatched)
            {
                matchedCents = 1200f * MathF.Log2(matchedFreq / targetFreqHz);
                FormatNoteDisplay(matchedFreq);
                return;
            }
        }

        if (mpmValid)
        {
            _liveNote = $"{mpm.NoteName}{mpm.Octave}";
            _liveFreqHz = mpm.FreqHz;
            _liveCents = mpm.Cents;
        }
        else if (combMatched)
        {
            FormatNoteDisplay(combFreq);
        }
        else
        {
            _liveNote = "--";
            _liveFreqHz = 0f;
            _liveCents = 0f;
        }
    }

    private float PeakHarmonicBin(float freqHz, float binHz)
    {
        int center = Math.Clamp((int)MathF.Round(freqHz / binHz), 1, CombFftN / 2 - 1);
        int lo = Math.Max(1, center - 1);
        int hi = Math.Min(CombFftN / 2 - 1, center + 1);
        float maxM = 0f;
        for (int k = lo; k <= hi; k++)
        {
            if (k * binHz is >= 55f and <= 65f) continue;
            if (_combMag[k] > maxM) maxM = _combMag[k];
        }
        return maxM;
    }

    private float ScoreHarmonicComb(float f0, float binHz, out float lowFrac)
    {
        float sumWeighted = 0f;
        float sumRaw = 0f;
        float sumLow = 0f;

        for (int h = 1; h <= 5; h++)
        {
            float m = PeakHarmonicBin(f0 * h, binHz);
            sumWeighted += CombWeights[h - 1] * m;
            sumRaw += m;
            if (h <= 2) sumLow += m;
        }
        lowFrac = sumRaw > 1e-8f ? sumLow / sumRaw : 0f;
        return sumWeighted;
    }

    private void FormatNoteDisplay(float freq)
    {
        if (freq <= 20f || !float.IsFinite(freq))
        {
            _liveNote = "--";
            _liveFreqHz = 0f;
            _liveCents = 0f;
            return;
        }
        double midiFloat = 69.0 + 12.0 * Math.Log2(freq / 440.0);
        int midi = (int)Math.Round(midiFloat);
        int noteIdx = ((midi % 12) + 12) % 12;
        int octave = midi / 12 - 1;
        _liveNote = $"{NoteNames[noteIdx]}{octave}";
        _liveFreqHz = MathF.Round(freq, 1);
        _liveCents = MathF.Round((float)((midiFloat - midi) * 100.0), 1);
    }

    private void ProcessSilenceChunk(ReadOnlySpan<float> samples, int sr, int targetCount, float bandRmsDb, float rawPeakDb)
    {
        const float quietBandRmsDb = -28f;
        const float quietPeakDb = -20f;

        if (bandRmsDb <= quietBandRmsDb && rawPeakDb <= quietPeakDb)
        {
            _isMatched = true;
            AppendStepSamples(samples);
            _statusHint = "조용한 상태 유지 중 — 노이즈 플로어와 60Hz 험을 분석하고 있습니다…";
            if (_stepCount >= targetCount)
            {
                FinalizeSilenceStep(_stepBuf.AsSpan(0, _stepCount), sr);
                AdvanceStep(sr);
            }
        }
        else
        {
            _isMatched = false;
            _silenceRetryWarnTicks++;
            if (_stepCount > 0 && _silenceRetryWarnTicks > 4)
                _stepCount = Math.Max(0, _stepCount - samples.Length / 2);
            _statusHint = $"입력이 감지됨 ({bandRmsDb:F0} dBFS) — 줄을 손으로 뮤트하고 조용히 대기하세요.";
        }
    }

    private void ProcessNoteChunk(
        StepSpec step,
        ReadOnlySpan<float> samples,
        int sr,
        int targetCount,
        float bandRmsDb,
        float rawPeakDb,
        bool pitchMatched,
        float matchedFreq,
        float matchedCents)
    {
        float gateFloor = Math.Max(-68f, _bandNoiseFloorDb + 6f);
        bool loudEnough = bandRmsDb >= gateFloor || rawPeakDb >= Math.Max(-58f, _bandNoiseFloorDb + 10f);

        if (!_noteLocked)
        {
            if (loudEnough && pitchMatched && matchedFreq > 20f)
            {
                _isMatched = true;
                _lockMissSamples = 0;
                AppendStepSamples(samples);
                _lockConfirmSamples += samples.Length;
                _matchedFreqSum += matchedFreq;
                _matchedFreqCount++;

                if (_lockConfirmSamples >= (int)(0.06f * sr))
                {
                    _noteLocked = true;
                    _dropSilenceSamples = 0;
                }

                _statusHint = $"음정 일치 ({step.TargetNote}, {matchedCents:+0;-0;0} ct) — 울림을 유지하세요…";
                if (_stepCount >= targetCount)
                {
                    _noteResults.Add(AnalyzeNoteStep(step, _stepBuf.AsSpan(0, _stepCount), sr));
                    AdvanceStep(sr);
                }
            }
            else
            {
                _isMatched = false;
                _lockMissSamples += samples.Length;
                if (_lockMissSamples >= (int)(0.14f * sr))
                {
                    _lockConfirmSamples = 0;
                    _stepCount = 0;
                    _matchedFreqSum = 0;
                    _matchedFreqCount = 0;
                }

                if (!loudEnough)
                    _statusHint = $"{step.Title} ({step.TargetFreqHz:F1} Hz)을 쳐주세요 (현재 {bandRmsDb:F0} dBFS / 감지 기준 {gateFloor:F0} dBFS)";
                else if (_liveFreqHz > 20f)
                {
                    float offset = 1200f * MathF.Log2(_liveFreqHz / step.TargetFreqHz);
                    _statusHint = offset > 0
                        ? $"현재 {_liveNote} ({_liveFreqHz:F1} Hz) — 목표 {step.TargetNote}보다 높습니다."
                        : $"현재 {_liveNote} ({_liveFreqHz:F1} Hz) — 목표 {step.TargetNote}보다 낮습니다.";
                }
                else
                    _statusHint = $"{step.TargetNote} 단음을 한 줄만 또렷하게 튕겨주세요 (레벨 {bandRmsDb:F0} dBFS).";
            }
            return;
        }

        // Phase 2: 음정이 한 번 락(Lock)된 뒤에는 자연 감쇠 중에도 끊김 없이 캡처 완료
        float sustainFloor = Math.Max(-76f, _bandNoiseFloorDb + 2f);
        if (bandRmsDb >= sustainFloor)
        {
            _dropSilenceSamples = 0;
            _isMatched = true;
            AppendStepSamples(samples);
            if (pitchMatched && matchedFreq > 20f)
            {
                _matchedFreqSum += matchedFreq;
                _matchedFreqCount++;
            }
            _statusHint = $"캡처 중 ({step.TargetNote}) — 줄이 울리도록 그대로 유지하세요…";
            if (_stepCount >= targetCount)
            {
                _noteResults.Add(AnalyzeNoteStep(step, _stepBuf.AsSpan(0, _stepCount), sr));
                AdvanceStep(sr);
            }
        }
        else
        {
            _dropSilenceSamples += samples.Length;
            if (_dropSilenceSamples > (int)(0.30f * sr))
            {
                if (_stepCount >= (int)(0.35f * sr) && _matchedFreqCount > 0)
                {
                    _noteResults.Add(AnalyzeNoteStep(step, _stepBuf.AsSpan(0, _stepCount), sr));
                    AdvanceStep(sr);
                }
                else
                {
                    _noteLocked = false;
                    _lockConfirmSamples = 0;
                    _stepCount = 0;
                    _matchedFreqSum = 0;
                    _matchedFreqCount = 0;
                    _isMatched = false;
                }
            }
        }
    }

    private void ProcessStrumChunk(ReadOnlySpan<float> samples, int sr, int targetCount, float bandRmsDb, float rawPeakDb)
    {
        float triggerPeakDb = Math.Max(-48f, _bandNoiseFloorDb + 10f);
        float triggerRmsDb = Math.Max(-56f, _bandNoiseFloorDb + 8f);
        if (!_strumTriggered && (rawPeakDb >= triggerPeakDb || bandRmsDb >= triggerRmsDb))
            _strumTriggered = true;

        if (_strumTriggered)
        {
            _isMatched = true;
            AppendStepSamples(samples);
            _statusHint = "스트럼 피크 및 광대역 스펙트럼 캡처 중… 그대로 울림을 유지하세요.";
            if (_stepCount >= targetCount)
            {
                FinalizeStrumStep(_stepBuf.AsSpan(0, _stepCount), sr);
                AdvanceStep(sr);
            }
        }
        else
        {
            _isMatched = false;
            _statusHint = $"Em 코드 또는 6줄 개방현을 평소 연주 최대 세기로 스트럼하세요 (현재 {rawPeakDb:F0} dBFS).";
        }
    }

    private void FinalizeSilenceStep(ReadOnlySpan<float> buf, int sr)
    {
        double sq = 0;
        for (int i = 0; i < buf.Length; i++)
            sq += (double)buf[i] * buf[i];
        float rms = MathF.Sqrt((float)(sq / Math.Max(1, buf.Length)));
        _noiseFloorDb = SanitizeDb(20f * MathF.Log10(MathF.Max(1e-6f, rms)), -96f, -24f);

        AccumulateAveragedFftPower(buf, out int framesUsed);
        if (framesUsed > 0)
        {
            float binHz = (float)sr / FftN;
            double humPow = SumBandPower(47f, 67f, binHz) + SumBandPower(113f, 127f, binHz);

            // 60/120/180Hz 등 협대역 험 고조파를 완전히 배제한 순수 광대역 노이즈 플로어 추정 (하위 60% 빈 평균)
            int kNoiseLo = Math.Clamp((int)MathF.Ceiling(135f / binHz), 1, FftN / 2 - 1);
            int kNoiseHi = Math.Clamp((int)MathF.Floor(2400f / binHz), kNoiseLo + 4, FftN / 2 - 1);
            int nBins = kNoiseHi - kNoiseLo + 1;
            var sortedBins = new double[nBins];
            for (int i = 0; i < nBins; i++)
                sortedBins[i] = _fftPowerAccum[kNoiseLo + i];
            Array.Sort(sortedBins);
            int keepCount = Math.Max(1, (int)(nBins * 0.60));
            double trimmedSum = 0;
            for (int i = 0; i < keepCount; i++)
                trimmedSum += sortedBins[i];
            double perBinNoise = trimmedSum / keepCount;
            int totalAudibleBins = CountBandBins(75f, 4500f, binHz);
            double bandRms = Math.Sqrt(Math.Max(1e-13, perBinNoise * totalAudibleBins * InvNormPow));
            _bandNoiseFloorDb = SanitizeDb(20f * MathF.Log10((float)bandRms), -98f, -28f);

            int humBins = CountBandBins(47f, 67f, binHz) + CountBandBins(113f, 127f, binHz);
            double humRms = Math.Sqrt(Math.Max(1e-13, humPow * InvNormPow));
            double scaledNoisePerHumBw = Math.Max(1e-13, (perBinNoise * humBins * InvNormPow));
            _hum60HzDb = SanitizeDb(20f * MathF.Log10((float)humRms), -98f, -20f);
            float midEquivDb = SanitizeDb(10f * MathF.Log10((float)scaledNoisePerHumBw), -105f, -25f);
            _humProminenceDb = _hum60HzDb - midEquivDb;
        }
        else
        {
            _bandNoiseFloorDb = Math.Clamp(_noiseFloorDb, -98f, -28f);
        }
        _silenceMeasured = true;
    }

    private GuitarNoteMeasurementDto AnalyzeNoteStep(StepSpec step, ReadOnlySpan<float> buf, int sr)
    {
        ComputeLevels(buf, out float peakDb, out float rmsDb);
        float detectedFreq = _matchedFreqCount > 0 ? (float)(_matchedFreqSum / _matchedFreqCount) : step.TargetFreqHz;
        float cents = step.TargetFreqHz > 20f && detectedFreq > 20f
            ? MathF.Round(1200f * MathF.Log2(detectedFreq / step.TargetFreqHz), 1)
            : 0f;
        float sustain = EstimateSustainSec(buf, sr);

        int steadyStart = Math.Min(Math.Max(0, buf.Length - FftN), (int)(0.08f * sr));
        var steady = buf.Slice(steadyStart, Math.Min(FftN, buf.Length - steadyStart));
        AccumulateAveragedFftPower(steady, out _);

        float binHz = (float)sr / FftN;
        float centroid = ComputeSpectralCentroid(binHz, 65f, 7500f);
        double f0 = Math.Max(40.0, detectedFreq);
        double h1 = ExtractHarmonicMag(f0, binHz);
        double h2 = ExtractHarmonicMag(f0 * 2.0, binHz);
        double h3 = ExtractHarmonicMag(f0 * 3.0, binHz);
        float h2Db = SanitizeDb(20f * MathF.Log10((float)(Math.Max(1e-6, h2) / Math.Max(1e-6, h1))), -40f, 20f);
        float h3Db = SanitizeDb(20f * MathF.Log10((float)(Math.Max(1e-6, h3) / Math.Max(1e-6, h1))), -40f, 20f);

        _noteBandList.Add(ExtractFiveBandPower(binHz));

        return new GuitarNoteMeasurementDto
        {
            StepId = step.Id,
            TargetLabel = step.Title,
            TargetNote = step.TargetNote,
            TargetFreqHz = MathF.Round(step.TargetFreqHz, 2),
            DetectedFreqHz = MathF.Round(detectedFreq, 2),
            CentsOffset = cents,
            PeakDb = MathF.Round(peakDb, 1),
            RmsDb = MathF.Round(rmsDb, 1),
            SustainSec = MathF.Round(sustain, 2),
            SpectralCentroidHz = MathF.Round(centroid, 0),
            H2RatioDb = MathF.Round(h2Db, 1),
            H3RatioDb = MathF.Round(h3Db, 1),
            Skipped = false,
        };
    }

    private void FinalizeStrumStep(ReadOnlySpan<float> buf, int sr)
    {
        ComputeLevels(buf, out float peakDb, out float rmsDb);
        _strumPeakDb = peakDb;
        _strumRmsDb = rmsDb;

        int steadyStart = Math.Min(Math.Max(0, buf.Length - FftN), (int)(0.05f * sr));
        AccumulateAveragedFftPower(buf.Slice(steadyStart), out _);
        float binHz = (float)sr / FftN;
        _strumCentroidHz = ComputeSpectralCentroid(binHz, 65f, 7500f);
        var b = ExtractFiveBandPower(binHz);
        Array.Copy(b, _strumBands, 5);
        _strumMeasured = true;
    }

    private void AdvanceStep(int sr)
    {
        _stepCount = 0;
        _matchedFreqSum = 0;
        _matchedFreqCount = 0;
        _noteLocked = false;
        _lockConfirmSamples = 0;
        _lockMissSamples = 0;
        _dropSilenceSamples = 0;
        _strumTriggered = false;
        _silenceRetryWarnTicks = 0;
        _isMatched = false;
        _detector.Reset();
        Array.Clear(_pitchWindow);

        if (_stepIndex + 1 < Steps.Length)
        {
            _stepIndex++;
            _cooldownSamples = (int)(0.22f * sr);
            _statusHint = Steps[_stepIndex].Instruction;
        }
        else
        {
            _completed = true;
            _completedProfile = BuildProfile();
            _statusHint = "기타 분석이 완료되었습니다! 결과를 확인하고 프로필을 저장하세요.";
        }
    }

    private GuitarProfileDto BuildProfile()
    {
        var activeNotes = _noteResults.Where(n => !n.Skipped).ToList();

        float peakDb = _strumMeasured
            ? _strumPeakDb
            : (activeNotes.Count > 0 ? activeNotes.Max(n => n.PeakDb) + 3.0f : -14f);
        float rmsDb = _strumMeasured
            ? _strumRmsDb
            : (activeNotes.Count > 0 ? activeNotes.Average(n => n.RmsDb) + 1.5f : -24f);
        float crestDb = Math.Clamp(peakDb - rmsDb, 3f, 24f);

        float sustain = activeNotes.Count > 0
            ? activeNotes.Average(n => n.SustainSec)
            : 2.8f;

        float centroid = _strumMeasured && activeNotes.Count > 0
            ? _strumCentroidHz * 0.55f + activeNotes.Average(n => n.SpectralCentroidHz) * 0.45f
            : _strumMeasured
                ? _strumCentroidHz
                : (activeNotes.Count > 0 ? activeNotes.Average(n => n.SpectralCentroidHz) : 1100f);

        // 5밴드 정규화 EQ 프로필 산출
        double[] combined = new double[5];
        double[] noteAvg = new double[5];
        if (_noteBandList.Count > 0)
        {
            foreach (var nb in _noteBandList)
                for (int i = 0; i < 5; i++) noteAvg[i] += nb[i];
            for (int i = 0; i < 5; i++) noteAvg[i] /= _noteBandList.Count;
        }

        if (_strumMeasured && _noteBandList.Count > 0)
        {
            double strumTotal = Math.Max(1e-12, _strumBands.Sum());
            double noteTotal = Math.Max(1e-12, noteAvg.Sum());
            for (int i = 0; i < 5; i++)
                combined[i] = 0.60 * (_strumBands[i] / strumTotal) + 0.40 * (noteAvg[i] / noteTotal);
        }
        else if (_strumMeasured)
        {
            double strumTotal = Math.Max(1e-12, _strumBands.Sum());
            for (int i = 0; i < 5; i++) combined[i] = _strumBands[i] / strumTotal;
        }
        else if (_noteBandList.Count > 0)
        {
            double noteTotal = Math.Max(1e-12, noteAvg.Sum());
            for (int i = 0; i < 5; i++) combined[i] = noteAvg[i] / noteTotal;
        }
        else
        {
            for (int i = 0; i < 5; i++) combined[i] = 0.2;
        }

        float[] bandDb = new float[5];
        double dbSum = 0;
        for (int i = 0; i < 5; i++)
        {
            float rawDb = 10f * MathF.Log10((float)Math.Max(1e-5, combined[i]));
            bandDb[i] = rawDb;
            dbSum += rawDb;
        }
        float meanBandDb = (float)(dbSum / 5.0);
        for (int i = 0; i < 5; i++)
            bandDb[i] = MathF.Round(Math.Clamp(bandDb[i] - meanBandDb, -18f, 18f), 1);

        // 제안 Input Trim (피크 -8 dBFS 타겟) & Noise Gate
        float suggestedTrim = MathF.Round(Math.Clamp(-8.0f - peakDb, -9.0f, 9.0f), 1);
        float noiseFloor = _silenceMeasured ? _noiseFloorDb : -74f;
        float hum60 = _silenceMeasured ? _hum60HzDb : -84f;
        float suggestedGate = MathF.Round(Math.Clamp(noiseFloor + 10.0f, -75.0f, -38.0f), 1);

        // 출력 / 밝기 / 따뜻함 스코어 (0..100) — 드라이 DI 기타 물리 기준 스케일링
        int outputScore = (int)Math.Clamp(MathF.Round((peakDb - (-32f)) / ((-4f) - (-32f)) * 100f), 0f, 100f);

        float hiVsLowTilt = (bandDb[3] + bandDb[4]) - (bandDb[0] + bandDb[1]);
        float rawBright = ((centroid - 220f) / 980f) * 58f + ((hiVsLowTilt + 24f) / 24f) * 42f;
        int brightnessScore = (int)Math.Clamp(MathF.Round(rawBright), 0f, 100f);

        float lowVsHiTilt = (bandDb[0] + bandDb[1]) - (bandDb[3] * 0.6f + bandDb[4]);
        float rawWarm = ((lowVsHiTilt + 2f) / 24f) * 58f + (100f - brightnessScore) * 0.42f;
        int warmthScore = (int)Math.Clamp(MathF.Round(rawWarm), 0f, 100f);

        // 픽업 성향 분류
        bool hasSingleCoilHum = _silenceMeasured && _humProminenceDb >= 10f && hum60 > -66f;
        string pickupType;
        if (hasSingleCoilHum)
            pickupType = outputScore >= 60 ? "Hot Single-Coil / P-90" : "Vintage Single-Coil";
        else if (outputScore >= 68 && brightnessScore < 65)
            pickupType = "High-Output Modern Humbucker";
        else if (brightnessScore >= 62 && outputScore < 58)
            pickupType = "Vintage Single-Coil (저노이즈/Noiseless)";
        else if (brightnessScore >= 55 && outputScore >= 58 && outputScore < 68 && warmthScore < 55)
            pickupType = "Hot Single-Coil / P-90";
        else
            pickupType = "Classic PAF Humbucker";

        string recommendation = BuildToneRecommendation(
            pickupType, brightnessScore, warmthScore, outputScore,
            noiseFloor, hum60, suggestedTrim, suggestedGate, hasSingleCoilHum);

        return new GuitarProfileDto
        {
            Id = $"G{DateTime.Now:yyyyMMddHHmmss}",
            Name = _guitarName,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            EstimatedPickupType = pickupType,
            NoiseFloorDb = MathF.Round(noiseFloor, 1),
            Hum60HzDb = MathF.Round(hum60, 1),
            SuggestedGateDb = suggestedGate,
            PeakDb = MathF.Round(peakDb, 1),
            RmsDb = MathF.Round(rmsDb, 1),
            CrestFactorDb = MathF.Round(crestDb, 1),
            SuggestedInputTrimDb = suggestedTrim,
            BrightnessScore = brightnessScore,
            WarmthScore = warmthScore,
            OutputPowerScore = outputScore,
            SustainSec = MathF.Round(sustain, 2),
            SpectralCentroidHz = MathF.Round(centroid, 0),
            LowBandDb = bandDb[0],
            LowMidBandDb = bandDb[1],
            MidBandDb = bandDb[2],
            HighMidBandDb = bandDb[3],
            TrebleBandDb = bandDb[4],
            ToneRecommendation = recommendation,
            Notes = _noteResults.Select(n => n.Clone()).ToList(),
        };
    }

    private static string BuildToneRecommendation(
        string pickupType,
        int brightness,
        int warmth,
        int outputPower,
        float noiseFloor,
        float hum60,
        float trimDb,
        float gateDb,
        bool hasHum)
    {
        var parts = new List<string>
        {
            $"추정 픽업 성향은 '{pickupType}'입니다 (밝기 {brightness}/100, 따뜻함 {warmth}/100, 출력 {outputPower}/100)."
        };

        if (MathF.Abs(trimDb) >= 1.0f)
            parts.Add($"입력 헤드룸을 맞추기 위해 Input Trim {trimDb:+0.0;-0.0;0.0} dB 보정을 권장합니다.");
        else
            parts.Add("입력 레벨이 이상적인 구간(-8 dBFS 부근)이라 Input Trim 0 dB 근처가 적합합니다.");

        if (hasHum || noiseFloor > -58f)
            parts.Add($"60Hz 험({hum60:F0} dBFS)과 배경 잡음({noiseFloor:F0} dBFS)이 감지되어 Noise Gate {gateDb:F0} dB 설정을 추천합니다.");
        else
            parts.Add($"배경 잡음이 매우 정숙하여({noiseFloor:F0} dBFS) 게이트를 가볍게({gateDb:F0} dB 이하) 두어도 다이내믹이 잘 살아납니다.");

        if (pickupType.Contains("Single-Coil", StringComparison.OrdinalIgnoreCase))
            parts.Add("튜브스크리머/클론 계열 드라이브로 중음역(750~1kHz)을 살짝 보강하거나 Fender/Plexi 성향 앰프에서 Presence를 과하지 않게 잡으면 탄탄한 톤을 얻기 좋습니다.");
        else if (outputPower >= 68)
            parts.Add("고출력 픽업 특성이므로 앰프 Pre Gain을 과하게 올리기보다 저역 타이트닝용 TS/SD-1 부스트(드라이브 낮음, 레벨 높음)를 앞단에 두면 명료한 타격감을 얻을 수 있습니다.");
        else
            parts.Add("균형 잡힌 험버커 대역폭을 지녀 크런치부터 리드까지 앰프 기본 EQ만으로도 안정적인 펀치감을 내기 좋습니다.");

        return string.Join(" ", parts);
    }

    public GuitarAnalysisLiveStateDto GetLiveState()
    {
        var step = Steps[Math.Clamp(_stepIndex, 0, Steps.Length - 1)];
        int sr = Math.Max(8000, _sampleRate);
        int targetCount = Math.Max(1, (int)MathF.Round(step.HoldSec * sr));
        float pct = _completed
            ? 100f
            : Math.Clamp((float)_stepCount / targetCount * 100f, 0f, 99.5f);

        return new GuitarAnalysisLiveStateDto
        {
            Active = _active,
            Completed = _completed,
            StepIndex = _stepIndex,
            TotalSteps = Steps.Length,
            StepId = step.Id,
            StepTitle = step.Title,
            Instruction = step.Instruction,
            TargetNote = step.TargetNote,
            TargetFreqHz = step.TargetFreqHz,
            ProgressPct = MathF.Round(pct, 1),
            IsMatched = _isMatched,
            LiveNote = _liveNote,
            LiveFreqHz = MathF.Round(_liveFreqHz, 1),
            LiveCents = MathF.Round(_liveCents, 1),
            LiveLevelDb = MathF.Round(_liveLevelDb, 1),
            LivePeakDb = MathF.Round(_recentPeakDb, 1),
            StatusHint = _statusHint,
            DraftProfile = _completedProfile?.Clone(),
        };
    }

    private void PushToPitchWindow(ReadOnlySpan<float> samples)
    {
        int n = samples.Length;
        if (n >= _pitchWindow.Length)
        {
            samples.Slice(n - _pitchWindow.Length).CopyTo(_pitchWindow);
            return;
        }
        int keep = _pitchWindow.Length - n;
        Array.Copy(_pitchWindow, n, _pitchWindow, 0, keep);
        samples.CopyTo(_pitchWindow.AsSpan(keep));
    }

    private void AppendStepSamples(ReadOnlySpan<float> samples)
    {
        int avail = _stepBuf.Length - _stepCount;
        if (avail <= 0) return;
        int copy = Math.Min(avail, samples.Length);
        samples.Slice(0, copy).CopyTo(_stepBuf.AsSpan(_stepCount));
        _stepCount += copy;
    }

    private void AccumulateAveragedFftPower(ReadOnlySpan<float> buf, out int framesUsed)
    {
        Array.Clear(_fftPowerAccum);
        framesUsed = 0;
        if (buf.IsEmpty) return;

        int maxFrames = Math.Min(4, Math.Max(1, buf.Length / FftN));
        int step = Math.Max(1, (buf.Length - Math.Min(buf.Length, FftN)) / Math.Max(1, maxFrames));

        for (int f = 0; f < maxFrames; f++)
        {
            int offset = buf.Length <= FftN ? 0 : Math.Min(buf.Length - FftN, f * step);
            int copy = Math.Min(FftN, buf.Length - offset);
            Array.Clear(_fftRe);
            Array.Clear(_fftIm);
            for (int i = 0; i < copy; i++)
                _fftRe[i] = buf[offset + i] * _hann[i];

            VisualizerAnalyzer.FftInPlace(_fftRe, _fftIm);
            for (int k = 1; k < FftN / 2; k++)
                _fftPowerAccum[k] += (double)_fftRe[k] * _fftRe[k] + (double)_fftIm[k] * _fftIm[k];
            framesUsed++;
        }

        if (framesUsed > 1)
        {
            double inv = 1.0 / framesUsed;
            for (int k = 1; k < FftN / 2; k++)
                _fftPowerAccum[k] *= inv;
        }
    }

    private double SumBandPower(float fLo, float fHi, float binHz)
    {
        int kLo = Math.Clamp((int)MathF.Floor(fLo / binHz), 1, FftN / 2 - 1);
        int kHi = Math.Clamp((int)MathF.Ceiling(fHi / binHz), kLo, FftN / 2 - 1);
        double sum = 0;
        for (int k = kLo; k <= kHi; k++)
            sum += _fftPowerAccum[k];
        return sum;
    }

    private static int CountBandBins(float fLo, float fHi, float binHz)
    {
        int kLo = Math.Clamp((int)MathF.Floor(fLo / binHz), 1, FftN / 2 - 1);
        int kHi = Math.Clamp((int)MathF.Ceiling(fHi / binHz), kLo, FftN / 2 - 1);
        return Math.Max(1, kHi - kLo + 1);
    }

    private float ComputeSpectralCentroid(float binHz, float fMin, float fMax)
    {
        int kLo = Math.Clamp((int)MathF.Floor(fMin / binHz), 1, FftN / 2 - 1);
        int kHi = Math.Clamp((int)MathF.Ceiling(fMax / binHz), kLo + 1, FftN / 2 - 1);
        double weighted = 0, total = 0;
        for (int k = kLo; k <= kHi; k++)
        {
            double mag = Math.Sqrt(Math.Max(0.0, _fftPowerAccum[k]));
            weighted += (k * binHz) * mag;
            total += mag;
        }
        return total > 1e-9 ? Math.Clamp((float)(weighted / total), 80f, 8000f) : 1000f;
    }

    private double ExtractHarmonicMag(double targetHz, float binHz)
    {
        int center = Math.Clamp((int)Math.Round(targetHz / binHz), 1, FftN / 2 - 1);
        int lo = Math.Max(1, center - 2);
        int hi = Math.Min(FftN / 2 - 1, center + 2);
        double pow = 0;
        for (int k = lo; k <= hi; k++)
            pow += _fftPowerAccum[k];
        return Math.Sqrt(Math.Max(0.0, pow));
    }

    private double[] ExtractFiveBandPower(float binHz) => new[]
    {
        SumBandPower(70f, 200f, binHz),
        SumBandPower(200f, 500f, binHz),
        SumBandPower(500f, 1200f, binHz),
        SumBandPower(1200f, 2800f, binHz),
        SumBandPower(2800f, 7500f, binHz),
    };

    private static void ComputeLevels(ReadOnlySpan<float> buf, out float peakDb, out float rmsDb)
    {
        if (buf.IsEmpty)
        {
            peakDb = -90f;
            rmsDb = -90f;
            return;
        }
        float maxAbs = 0f;
        double sumSq = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            float v = buf[i];
            sumSq += (double)v * v;
            float a = MathF.Abs(v);
            if (a > maxAbs) maxAbs = a;
        }
        float rms = MathF.Sqrt((float)(sumSq / buf.Length));
        peakDb = SanitizeDb(20f * MathF.Log10(MathF.Max(1e-5f, maxAbs)), -90f, 0f);
        rmsDb = SanitizeDb(20f * MathF.Log10(MathF.Max(1e-5f, rms)), -90f, 0f);
    }

    private static float EstimateSustainSec(ReadOnlySpan<float> buf, int sr)
    {
        int hop = Math.Max(64, sr / 20); // 50ms 프레임
        int numFrames = buf.Length / hop;
        if (numFrames < 6) return 2.8f;

        Span<float> frameDb = numFrames <= 128 ? stackalloc float[numFrames] : new float[numFrames];
        int peakIdx = 0;
        float peakFrameDb = -120f;

        for (int f = 0; f < numFrames; f++)
        {
            int start = f * hop;
            double sq = 0;
            for (int i = 0; i < hop; i++)
            {
                float s = buf[start + i];
                sq += (double)s * s;
            }
            float db = 20f * MathF.Log10(MathF.Max(1e-5f, MathF.Sqrt((float)(sq / hop))));
            frameDb[f] = db;
            if (db > peakFrameDb)
            {
                peakFrameDb = db;
                peakIdx = f;
            }
        }

        int startIdx = Math.Min(numFrames - 4, peakIdx + 2);
        int nPts = numFrames - startIdx;
        if (nPts < 4) return 2.8f;

        double sumT = 0, sumY = 0, sumTT = 0, sumTY = 0;
        for (int i = 0; i < nPts; i++)
        {
            double t = i * (hop / (double)sr);
            double y = frameDb[startIdx + i];
            sumT += t;
            sumY += y;
            sumTT += t * t;
            sumTY += t * y;
        }
        double denom = nPts * sumTT - sumT * sumT;
        if (Math.Abs(denom) < 1e-9) return 3.2f;
        double slopeDbPerSec = (nPts * sumTY - sumT * sumY) / denom;
        if (!double.IsFinite(slopeDbPerSec) || slopeDbPerSec >= -0.4)
            return 8.0f;

        float t20 = (float)(20.0 / Math.Abs(slopeDbPerSec));
        return Math.Clamp(t20, 0.5f, 10.0f);
    }

    private static float SanitizeDb(float v, float minDb, float maxDb)
        => float.IsFinite(v) ? Math.Clamp(v, minDb, maxDb) : minDb;
}
