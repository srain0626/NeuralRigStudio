using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using NAudio.Wave;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 7종의 실제 마이크 수음 실측 WAV 캐비넷 IR 뱅크 및 Zero-Allocation 스테레오 SIMD 컨볼버.
/// - 어셈블리에 내장된 실제 캐비넷/마이크(SM57/SM7B/Soundwoofer) WAV 임펄스 응답을
///   Prepare 시점에 디코딩·리샘플·위상정렬·정규화(1024-tap)하여 로드한다.
/// - RT 스레드 할당: 0 B.
/// </summary>
public sealed class CabinetIrBank
{
    public const int TapCount = 1024;
    public const int MaxWavTapCount = PartitionedIr.MaxTaps;
    public const int ModelCount = 8; // indices 0..7 (1 = custom placeholder -> falls back to 0)

    private readonly float[][] _irs = new float[ModelCount][];
    private readonly float[][] _irsRev = new float[ModelCount][];
    private readonly PartitionedIr?[] _partIrs = new PartitionedIr?[ModelCount];
    private readonly bool[] _isMeasured = new bool[ModelCount];
    private int _sampleRate;

    public bool IsMeasuredIr(int subModel) => _isMeasured[Math.Clamp(subModel, 0, ModelCount - 1)];

    public void Prepare(int sampleRate)
    {
        sampleRate = Math.Max(8000, sampleRate);
        if (_sampleRate == sampleRate && _irsRev[0] != null) return;
        _sampleRate = sampleRate;

        // 0: 4x12 V30 Modern (Kalthallen 4x12 Celestion V30, Shure SM57 실측 WAV)
        LoadMeasuredOrFallback(0, "NeuralRigStudio.CabIRs.cab_v30.wav", sampleRate, () =>
            BuildModel(0, sampleRate, hpHz: 82, hpQ: 1.4, bodyHz: 115, bodyAmt: 0.50, midHz: 2350, midW: 850, midAmt: 0.65,
                dipHz: 3900, dipW: 520, dipAmt: 0.35, lpHz: 4700, lpOrder: 4.0, erDelayMs: 1.4, erGain: -0.10));

        // 1: Custom IR fallback = slot 0 실측 V30 IR
        _irs[1] = _irs[0];
        _irsRev[1] = _irsRev[0];
        _partIrs[1] = _partIrs[0];
        _isMeasured[1] = _isMeasured[0];

        // 2: 2x12 JC-120 Open Back (GK09 Roland Jazz Chorus, Shure SM7B 실측 WAV)
        LoadMeasuredOrFallback(2, "NeuralRigStudio.CabIRs.cab_jc120.wav", sampleRate, () =>
            BuildModel(2, sampleRate, hpHz: 68, hpQ: 1.8, bodyHz: 128, bodyAmt: 0.22, midHz: 3200, midW: 1400, midAmt: 0.35,
                dipHz: 420, dipW: 220, dipAmt: 0.18, lpHz: 6800, lpOrder: 3.2, erDelayMs: 2.3, erGain: 0.08));

        // 3: 2x12 VOX Alnico Blue (VOX AC30 2x12 internal speaker, Shure SM57 실측 WAV)
        LoadMeasuredOrFallback(3, "NeuralRigStudio.CabIRs.cab_alnico.wav", sampleRate, () =>
            BuildModel(3, sampleRate, hpHz: 88, hpQ: 1.6, bodyHz: 135, bodyAmt: 0.35, midHz: 1950, midW: 680, midAmt: 0.78,
                dipHz: 650, dipW: 260, dipAmt: 0.22, lpHz: 5300, lpOrder: 3.6, erDelayMs: 2.0, erGain: 0.11));

        // 4: Marshall 1960A (Kalthallen 4x12 Celestion G12T-75, Shure SM57 실측 WAV)
        LoadMeasuredOrFallback(4, "NeuralRigStudio.CabIRs.cab_1960a.wav", sampleRate, () =>
            BuildModel(4, sampleRate, hpHz: 78, hpQ: 1.35, bodyHz: 105, bodyAmt: 0.58, midHz: 3750, midW: 950, midAmt: 0.62,
                dipHz: 780, dipW: 340, dipAmt: 0.34, lpHz: 5400, lpOrder: 4.0, erDelayMs: 1.65, erGain: -0.12));

        // 5: Mesa/Boogie Rectifier 4x12 Oversized ([SP] MB OS 4x12 V30, Shure SM57 실측 WAV)
        LoadMeasuredOrFallback(5, "NeuralRigStudio.CabIRs.cab_mesa412.wav", sampleRate, () =>
            BuildModel(5, sampleRate, hpHz: 68, hpQ: 1.25, bodyHz: 100, bodyAmt: 0.72, midHz: 2200, midW: 780, midAmt: 0.70,
                dipHz: 3650, dipW: 480, dipAmt: 0.32, lpHz: 4550, lpOrder: 4.2, erDelayMs: 1.85, erGain: -0.14));

        // 6: 1x12 Fender Jensen Open Back (Soundwoofer Fender Deluxe Reverb 1x12 실측 WAV)
        LoadMeasuredOrFallback(6, "NeuralRigStudio.CabIRs.cab_jensen112.wav", sampleRate, () =>
            BuildModel(6, sampleRate, hpHz: 94, hpQ: 1.7, bodyHz: 145, bodyAmt: 0.42, midHz: 3400, midW: 1100, midAmt: 0.52,
                dipHz: 540, dipW: 240, dipAmt: 0.30, lpHz: 6000, lpOrder: 3.4, erDelayMs: 2.6, erGain: 0.10));

        // 7: Marshall 1960BV ([SP] MSH 60 4x12 V30, Shure SM57 실측 WAV)
        LoadMeasuredOrFallback(7, "NeuralRigStudio.CabIRs.cab_1960bv.wav", sampleRate, () =>
            BuildModel(7, sampleRate, hpHz: 80, hpQ: 1.35, bodyHz: 130, bodyAmt: 0.56, midHz: 2050, midW: 720, midAmt: 0.72,
                dipHz: 4100, dipW: 500, dipAmt: 0.30, lpHz: 4850, lpOrder: 4.0, erDelayMs: 1.30, erGain: -0.11));
    }

    private void LoadMeasuredOrFallback(int slot, string resourceName, int sampleRate, Action fallback)
    {
        if (TryLoadEmbeddedWavIr(resourceName, sampleRate, MaxWavTapCount, out var ir, out var rev))
        {
            _irs[slot] = ir;
            _irsRev[slot] = rev;
            _partIrs[slot] = PartitionedIr.Compile(ir);
            _isMeasured[slot] = true;
            return;
        }
        _isMeasured[slot] = false;
        fallback();
        if (_irs[slot] is { Length: > 0 } fbIr)
            _partIrs[slot] = PartitionedIr.Compile(fbIr);
    }

    internal static bool TryLoadEmbeddedWavIr(string resourceName, int targetSampleRate, int maxTaps, out float[] ir, out float[] rev)
    {
        ir = Array.Empty<float>();
        rev = Array.Empty<float>();
        using var stream = typeof(CabinetIrBank).Assembly.GetManifestResourceStream(resourceName);
        return stream != null && TryReadWavStreamIr(stream, targetSampleRate, maxTaps, out ir, out rev);
    }

    public static bool TryLoadWavIrFromFile(string path, int targetSampleRate, out PartitionedIr? partIr)
    {
        partIr = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using var fs = File.OpenRead(path);
            if (!TryReadWavStreamIr(fs, Math.Max(8000, targetSampleRate), MaxWavTapCount, out var ir, out _))
                return false;
            partIr = PartitionedIr.Compile(ir);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadWavStreamIr(Stream stream, int targetSampleRate, int maxTaps, out float[] ir, out float[] rev)
    {
        ir = Array.Empty<float>();
        rev = Array.Empty<float>();

        using var reader = new WaveFileReader(stream);
        var sp = reader.ToSampleProvider();
        int srcRate = Math.Max(8000, sp.WaveFormat.SampleRate);
        int ch = Math.Max(1, sp.WaveFormat.Channels);
        int maxSrcFrames = srcRate * 2;
        var raw = new List<float>(Math.Min(maxSrcFrames, 65536));
        var chunk = new float[4096 * ch];
        int read;
        while ((read = sp.Read(chunk.AsSpan())) > 0 && raw.Count < maxSrcFrames)
        {
            int framesRead = read / ch;
            for (int i = 0; i < framesRead; i++)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++) sum += chunk[i * ch + c];
                raw.Add(sum / ch);
            }
        }
        if (raw.Count < 8) return false;

        float[] mono = raw.ToArray();
        if (srcRate != targetSampleRate)
            mono = SincResampler.Resample(mono, srcRate, targetSampleRate);

        float maxAbs = 0f;
        for (int i = 0; i < mono.Length; i++)
        {
            float a = MathF.Abs(mono[i]);
            if (a > maxAbs) maxAbs = a;
        }
        if (maxAbs < 1e-7f) return false;

        int start = 0;
        float thresh = maxAbs * 0.006f;
        for (int i = 0; i < Math.Min(mono.Length, targetSampleRate / 4); i++)
        {
            if (MathF.Abs(mono[i]) >= thresh)
            {
                start = Math.Max(0, i - 1);
                break;
            }
        }

        int len = Math.Min(maxTaps, mono.Length - start);
        if (len < 16)
        {
            start = 0;
            len = Math.Min(maxTaps, mono.Length);
        }

        ir = new float[len];
        Array.Copy(mono, start, ir, 0, len);

        int fade = Math.Min(64, len / 8);
        for (int i = 0; i < fade; i++)
            ir[len - fade + i] *= 1f - (float)i / fade;

        double e = 0.0;
        for (int i = 0; i < len; i++) e += (double)ir[i] * ir[i];
        float norm = (float)Math.Sqrt(e + 1e-12);
        if (norm > 1e-6f)
        {
            for (int i = 0; i < len; i++) ir[i] /= norm;
        }

        rev = new float[len];
        for (int i = 0; i < len; i++)
            rev[i] = ir[len - 1 - i];
        return true;
    }

    public float[] GetIr(int subModel)
    {
        int idx = Math.Clamp(subModel, 0, ModelCount - 1);
        return _irs[idx] ?? Array.Empty<float>();
    }

    public float[] GetIrRev(int subModel)
    {
        int idx = Math.Clamp(subModel, 0, ModelCount - 1);
        return _irsRev[idx] ?? Array.Empty<float>();
    }

    public PartitionedIr? GetPartitionedIr(int subModel)
    {
        int idx = Math.Clamp(subModel, 0, ModelCount - 1);
        return _partIrs[idx];
    }

    private void BuildModel(
        int slot,
        int sampleRate,
        double hpHz, double hpQ,
        double bodyHz, double bodyAmt,
        double midHz, double midW, double midAmt,
        double dipHz, double dipW, double dipAmt,
        double lpHz, double lpOrder,
        double erDelayMs, double erGain)
    {
        var ir = SynthesizeMinimumPhaseIr(
            sampleRate, hpHz, hpQ, bodyHz, bodyAmt,
            midHz, midW, midAmt, dipHz, dipW, dipAmt,
            lpHz, lpOrder, erDelayMs, erGain);

        var rev = new float[TapCount];
        for (int i = 0; i < TapCount; i++)
            rev[i] = ir[TapCount - 1 - i];

        _irs[slot] = ir;
        _irsRev[slot] = rev;
    }

    public static PartitionedIr CompileCustomCabIr(
        int sampleRate,
        double hpHz, double hpQ,
        double bodyHz, double bodyAmt,
        double midHz, double midAmt,
        double lpHz, double erDelayMs)
    {
        var ir = SynthesizeMinimumPhaseIr(
            Math.Max(8000, sampleRate),
            hpHz, hpQ, bodyHz, bodyAmt,
            midHz, midW: 780.0, midAmt: midAmt,
            dipHz: 3950.0, dipW: 500.0, dipAmt: 0.25,
            lpHz: lpHz, lpOrder: 4.0,
            erDelayMs: erDelayMs, erGain: erDelayMs > 1.9 ? 0.09 : -0.10);
        return PartitionedIr.Compile(ir);
    }

    public static float[] SynthesizeCustomCabIrFromMeasuredCurve(
        int sampleRate,
        ReadOnlySpan<(double freqHz, double db)> curve,
        double erDelayMs,
        double erGain)
    {
        sampleRate = Math.Max(8000, sampleRate);
        const int nFft = 1024;
        int bins = nFft / 2 + 1;
        double[] logMag = new double[nFft];

        // Sort curve by frequency using a small stack/list copy
        var pts = new (double f, double db)[curve.Length];
        for (int i = 0; i < curve.Length; i++) pts[i] = curve[i];
        Array.Sort(pts, (a, b) => a.f.CompareTo(b.f));

        double maxDb = -200.0;
        for (int i = 0; i < pts.Length; i++)
            if (pts[i].db > maxDb) maxDb = pts[i].db;
        if (!double.IsFinite(maxDb) || pts.Length == 0) maxDb = 0.0;

        for (int k = 0; k < bins; k++)
        {
            double f = k * (double)sampleRate / nFft;
            double db = pts.Length > 0 ? InterpolateCurveLogDb(pts, f) - maxDb : 0.0;
            double mag = Math.Clamp(Math.Pow(10.0, db / 20.0), 1e-4, 4.0);
            double lm = Math.Log(mag);
            logMag[k] = lm;
            if (k > 0 && k < nFft / 2)
                logMag[nFft - k] = lm;
        }

        return MinimumPhaseReconstructFromLogMag(logMag, sampleRate, erDelayMs, erGain);
    }

    public static PartitionedIr CompileCustomCabFromMeasuredCurve(
        int sampleRate,
        ReadOnlySpan<(double freqHz, double db)> curve,
        double erDelayMs,
        double erGain)
        => PartitionedIr.Compile(SynthesizeCustomCabIrFromMeasuredCurve(sampleRate, curve, erDelayMs, erGain));

    private static double InterpolateCurveLogDb(ReadOnlySpan<(double f, double db)> pts, double freqHz)
    {
        if (pts.Length == 1) return pts[0].db;
        double f = Math.Max(15.0, freqHz);
        if (f <= pts[0].f)
        {
            // Below lowest measured point: sub-bass 12 dB/oct natural speaker rolloff
            double oct = Math.Log2(Math.Max(1.0, pts[0].f) / f);
            return pts[0].db - 12.0 * oct;
        }
        int last = pts.Length - 1;
        if (f >= pts[last].f)
        {
            // Above highest measured point: cone breakup 18 dB/oct rolloff
            double oct = Math.Log2(f / Math.Max(1.0, pts[last].f));
            return pts[last].db - 18.0 * oct;
        }

        double lf = Math.Log(f);
        for (int i = 0; i < last; i++)
        {
            double f0 = Math.Max(1.0, pts[i].f);
            double f1 = Math.Max(f0 + 1e-3, pts[i + 1].f);
            if (f >= f0 && f <= f1)
            {
                double t = (lf - Math.Log(f0)) / (Math.Log(f1) - Math.Log(f0));
                return pts[i].db + t * (pts[i + 1].db - pts[i].db);
            }
        }
        return pts[last].db;
    }

    private static float[] SynthesizeMinimumPhaseIr(
        int sampleRate,
        double hpHz, double hpQ,
        double bodyHz, double bodyAmt,
        double midHz, double midW, double midAmt,
        double dipHz, double dipW, double dipAmt,
        double lpHz, double lpOrder,
        double erDelayMs, double erGain)
    {
        const int nFft = 1024;
        int bins = nFft / 2 + 1;
        double[] logMag = new double[nFft];

        for (int k = 0; k < bins; k++)
        {
            double f = k * (double)sampleRate / nFft;
            double rHp = f / hpHz;
            double magHp = f < 1.0 ? 1e-4 : (rHp * rHp) / Math.Sqrt(Math.Pow(1.0 - rHp * rHp, 2) + hpQ * rHp * rHp);

            double bRes = 1.0 + bodyAmt * Math.Exp(-Math.Pow(Math.Log(Math.Max(f, 20.0) / bodyHz) / 0.34, 2));
            double midPk = 1.0 + midAmt * Math.Exp(-Math.Pow((f - midHz) / midW, 2));
            double notch = 1.0 - dipAmt * Math.Exp(-Math.Pow((f - dipHz) / dipW, 2));
            double rLp = f / lpHz;
            double magLp = 1.0 / (1.0 + Math.Pow(rLp, lpOrder));

            double mag = Math.Clamp(magHp * bRes * midPk * notch * magLp, 1e-5, 5.0);
            double lm = Math.Log(mag);
            logMag[k] = lm;
            if (k > 0 && k < nFft / 2)
                logMag[nFft - k] = lm;
        }

        return MinimumPhaseReconstructFromLogMag(logMag, sampleRate, erDelayMs, erGain);
    }

    private static float[] MinimumPhaseReconstructFromLogMag(
        double[] logMag,
        int sampleRate,
        double erDelayMs,
        double erGain)
    {
        const int nFft = 1024;

        // Cepstral Minimum-Phase Reconstruction
        double[] cepIm = new double[nFft];
        FftD(logMag, cepIm, inverse: true);

        double[] wLogRe = new double[nFft];
        double[] wLogIm = new double[nFft];
        wLogRe[0] = logMag[0];
        wLogRe[nFft / 2] = logMag[nFft / 2];
        for (int i = 1; i < nFft / 2; i++)
            wLogRe[i] = 2.0 * logMag[i];

        FftD(wLogRe, wLogIm, inverse: false);

        double[] specRe = new double[nFft];
        double[] specIm = new double[nFft];
        for (int k = 0; k < nFft; k++)
        {
            double g = Math.Exp(wLogRe[k]);
            specRe[k] = g * Math.Cos(wLogIm[k]);
            specIm[k] = g * Math.Sin(wLogIm[k]);
        }

        FftD(specRe, specIm, inverse: true);

        var ir = new float[TapCount];
        int erSamples = Math.Clamp((int)Math.Round(erDelayMs * 0.001 * sampleRate), 8, TapCount - 32);
        int fade = 48;
        for (int i = 0; i < TapCount; i++)
        {
            double v = specRe[i];
            // 물리적 인클로저 초기 반사 (mic/baffle comb reflection)
            if (i >= erSamples)
                v += erGain * specRe[i - erSamples];
            if (i >= TapCount - fade)
                v *= (double)(TapCount - i) / fade;
            ir[i] = (float)v;
        }

        double energy = 0.0;
        for (int i = 0; i < TapCount; i++) energy += (double)ir[i] * ir[i];
        float norm = (float)Math.Sqrt(energy + 1e-12);
        for (int i = 0; i < TapCount; i++) ir[i] /= norm;
        return ir;
    }

    private static void FftD(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        double sign = inverse ? 1.0 : -1.0;
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = sign * 2.0 * Math.PI / len;
            double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
            int half = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1.0, curIm = 0.0;
                for (int j = 0; j < half; j++)
                {
                    int u = i + j, v = u + half;
                    double tRe = re[v] * curRe - im[v] * curIm;
                    double tIm = re[v] * curIm + im[v] * curRe;
                    re[v] = re[u] - tRe;
                    im[v] = im[u] - tIm;
                    re[u] += tRe;
                    im[u] += tIm;
                    double nRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nRe;
                }
            }
        }
        if (inverse)
        {
            double inv = 1.0 / n;
            for (int i = 0; i < n; i++) { re[i] *= inv; im[i] *= inv; }
        }
    }

    /// <summary>독립 슬롯용 Zero-Allocation 스테레오 SIMD 캐비넷 IR 컨볼버.</summary>
    public sealed class StereoCabConvolver
    {
        private float[] _histL = Array.Empty<float>();
        private float[] _histR = Array.Empty<float>();

        public void Prepare(int maxFrames)
        {
            int len = ToneProcessor.MaxCabTaps + Math.Max(256, maxFrames);
            _histL = new float[len];
            _histR = new float[len];
            Reset();
        }

        public void Reset()
        {
            if (_histL.Length > 0) Array.Clear(_histL);
            if (_histR.Length > 0) Array.Clear(_histR);
        }

        public void ConvolveStereo(Span<float> bufL, Span<float> bufR, int frames, float[] cabRev)
        {
            int cabLen = cabRev.Length;
            if (cabLen <= 0 || cabLen > ToneProcessor.MaxCabTaps || _histL.Length < cabLen + frames) return;

            int histN = cabLen - 1;
            // 모노 입력 Fast-Path: L과 R이 완전히 동일하면 1회만 컨볼루션하고 복사 (CPU 50% 절감)
            bool isMono = true;
            for (int i = 0; i < frames; i++)
            {
                if (MathF.Abs(bufL[i] - bufR[i]) > 1e-6f)
                {
                    isMono = false;
                    break;
                }
            }

            bufL.Slice(0, frames).CopyTo(_histL.AsSpan(histN, frames));
            if (isMono)
            {
                for (int i = 0; i < frames; i++)
                {
                    float y = DotSimd(_histL.AsSpan(i, cabLen), cabRev);
                    bufL[i] = y;
                    bufR[i] = y;
                }
                _histL.AsSpan(frames, histN).CopyTo(_histL.AsSpan(0, histN));
                _histL.AsSpan(0, histN).CopyTo(_histR.AsSpan(0, histN));
                return;
            }

            bufR.Slice(0, frames).CopyTo(_histR.AsSpan(histN, frames));
            for (int i = 0; i < frames; i++)
            {
                bufL[i] = DotSimd(_histL.AsSpan(i, cabLen), cabRev);
                bufR[i] = DotSimd(_histR.AsSpan(i, cabLen), cabRev);
            }
            _histL.AsSpan(frames, histN).CopyTo(_histL.AsSpan(0, histN));
            _histR.AsSpan(frames, histN).CopyTo(_histR.AsSpan(0, histN));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DotSimd(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            int len = a.Length;
            int n = Vector<float>.Count;
            int i = 0;
            var vacc = Vector<float>.Zero;
            if (Vector.IsHardwareAccelerated && len >= n)
            {
                for (; i <= len - n; i += n)
                {
                    var va = new Vector<float>(a.Slice(i, n));
                    var vb = new Vector<float>(b.Slice(i, n));
                    vacc += va * vb;
                }
            }
            float sum = Vector.Dot(vacc, Vector<float>.One);
            for (; i < len; i++)
                sum += a[i] * b[i];
            return sum;
        }
    }
}
