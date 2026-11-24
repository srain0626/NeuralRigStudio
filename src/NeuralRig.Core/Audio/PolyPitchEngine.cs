using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

internal sealed class Radix2Fft1024
{
    public const int N = 1024;
    private readonly int[] _bitRev = new int[N];
    private readonly float[] _cosFwd = new float[N / 2];
    private readonly float[] _sinFwd = new float[N / 2];

    public Radix2Fft1024()
    {
        for (int i = 0; i < N; i++)
        {
            int x = i;
            int r = 0;
            for (int b = 0; b < 10; b++)
            {
                r = (r << 1) | (x & 1);
                x >>= 1;
            }
            _bitRev[i] = r;
        }
        for (int k = 0; k < N / 2; k++)
        {
            double ang = -2.0 * Math.PI * k / N;
            _cosFwd[k] = (float)Math.Cos(ang);
            _sinFwd[k] = (float)Math.Sin(ang);
        }
    }

    public void Forward(float[] re, float[] im) => Transform(re, im, inverse: false);
    public void Inverse(float[] re, float[] im) => Transform(re, im, inverse: true);

    private void Transform(float[] re, float[] im, bool inverse)
    {
        for (int i = 0; i < N; i++)
        {
            int j = _bitRev[i];
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        float invSign = inverse ? -1f : 1f;
        for (int len = 2; len <= N; len <<= 1)
        {
            int half = len >> 1;
            int step = N / len;
            for (int i = 0; i < N; i += len)
            {
                int twIdx = 0;
                for (int j = 0; j < half; j++)
                {
                    float wRe = _cosFwd[twIdx];
                    float wIm = _sinFwd[twIdx] * invSign;
                    twIdx += step;

                    int u = i + j;
                    int v = u + half;
                    float rv = re[v];
                    float iv = im[v];
                    float tRe = rv * wRe - iv * wIm;
                    float tIm = rv * wIm + iv * wRe;
                    re[v] = re[u] - tRe;
                    im[v] = im[u] - tIm;
                    re[u] += tRe;
                    im[u] += tIm;
                }
            }
        }

        if (inverse)
        {
            const float invN = 1f / N;
            for (int i = 0; i < N; i++)
            {
                re[i] *= invN;
                im[i] *= invN;
            }
        }
    }
}

/// <summary>
/// 실시간 Zero-Allocation 폴리포닉 STFT Phase Vocoder 피치 시프터.
/// - Virtual Capo (반음/센트 디튠), Whammy (연속 스위프), Octaver (-1/+1 옥타브 동시 합성)
/// - 1024-pt FFT / 256-sample Hop (4x Overlap Hann OLA), 모노 입력 시 CPU 50% Fast-Path
/// - RT 스레드 힙 할당: 0 B.
/// </summary>
public sealed class PolyPitchEngine
{
    public const int FftSize = Radix2Fft1024.N; // 1024
    public const int HopSize = 256;
    public const int Bins = FftSize / 2 + 1;    // 513
    private const int RingMask = FftSize - 1;
    private const float FreqPerBin = 2f * MathF.PI * HopSize / FftSize; // pi/2
    private const float OlaNorm = 1f / 1.5f;

    private sealed class ChannelState
    {
        public readonly float[] InRing = new float[FftSize];
        public readonly float[] OutRing = new float[FftSize];
        public readonly float[] DryRing = new float[FftSize];

        public readonly float[] FftRe = new float[FftSize];
        public readonly float[] FftIm = new float[FftSize];
        public readonly float[] Mag = new float[Bins];
        public readonly float[] TrueFreq = new float[Bins];
        public readonly float[] LastPhase = new float[Bins];

        public readonly float[] SynMagA = new float[Bins];
        public readonly float[] SynFreqA = new float[Bins];
        public readonly float[] SumPhaseA = new float[Bins];

        public readonly float[] SynMagB = new float[Bins];
        public readonly float[] SynFreqB = new float[Bins];
        public readonly float[] SumPhaseB = new float[Bins];

        public int InWrite;
        public int OutPos;
        public int DryPos;
        public int HopCount;
        public float LpState;

        public void Reset()
        {
            Array.Clear(InRing);
            Array.Clear(OutRing);
            Array.Clear(DryRing);
            Array.Clear(FftRe);
            Array.Clear(FftIm);
            Array.Clear(Mag);
            Array.Clear(TrueFreq);
            Array.Clear(LastPhase);
            Array.Clear(SynMagA);
            Array.Clear(SynFreqA);
            Array.Clear(SumPhaseA);
            Array.Clear(SynMagB);
            Array.Clear(SynFreqB);
            Array.Clear(SumPhaseB);
            InWrite = 0;
            OutPos = 0;
            DryPos = 0;
            HopCount = 0;
            LpState = 0f;
        }
    }

    private static readonly float[] HannWindow = BuildHannWindow();
    private readonly Radix2Fft1024 _fft = new();
    private readonly ChannelState _chL = new();
    private readonly ChannelState _chR = new();

    private int _sampleRate = 48000;
    private float _smoothRatio = 1f;
    private bool _ratioReady;

    private static float[] BuildHannWindow()
    {
        var w = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / FftSize));
        return w;
    }

    public void Prepare(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        Reset();
    }

    public void Reset()
    {
        _chL.Reset();
        _chR.Reset();
        _smoothRatio = 1f;
        _ratioReady = false;
    }

    /// <summary>Virtual Capo / Whammy 연속 피치 시프트 처리 (RT 할당 0).</summary>
    public void ProcessPitchShift(
        Span<float> bufL,
        Span<float> bufR,
        int frames,
        float semitones,
        float detuneCents,
        float mixPct,
        float levelDb)
    {
        if (frames <= 0) return;

        float totalSemis = Math.Clamp(semitones, -24f, 24f) + Math.Clamp(detuneCents, -50f, 50f) * 0.01f;
        float targetRatio = MathF.Pow(2f, totalSemis / 12f);
        if (!_ratioReady)
        {
            _smoothRatio = targetRatio;
            _ratioReady = true;
        }
        float ratioAlpha = 1f - MathF.Exp(-frames / MathF.Max(1f, 0.012f * _sampleRate));
        _smoothRatio += ratioAlpha * (targetRatio - _smoothRatio);

        float levelGain = MathF.Pow(10f, Math.Clamp(levelDb, -12f, 12f) / 20f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;

        if (MathF.Abs(targetRatio - 1f) < 1e-4f && MathF.Abs(_smoothRatio - 1f) < 1e-4f)
        {
            _smoothRatio = 1f;
            for (int i = 0; i < frames; i++)
            {
                float xL = bufL[i];
                float xR = bufR[i];
                // Keep dry/in rings synchronized so engaging pitch later is click-free
                _chL.InRing[_chL.InWrite] = xL;
                _chL.InWrite = (_chL.InWrite + 1) & RingMask;
                _chL.DryRing[_chL.DryPos] = xL;
                _chL.DryPos = (_chL.DryPos + 1) & RingMask;
                _chR.InRing[_chR.InWrite] = xR;
                _chR.InWrite = (_chR.InWrite + 1) & RingMask;
                _chR.DryRing[_chR.DryPos] = xR;
                _chR.DryPos = (_chR.DryPos + 1) & RingMask;
                bufL[i] = xL * levelGain;
                bufR[i] = xR * levelGain;
            }
            return;
        }

        bool isMono = IsMonoBlock(bufL, bufR, frames);
        ProcessChannelPitch(_chL, bufL, frames, _smoothRatio, mix, levelGain);
        if (isMono)
        {
            bufL.Slice(0, frames).CopyTo(bufR.Slice(0, frames));
            MirrorState(_chL, _chR);
        }
        else
        {
            ProcessChannelPitch(_chR, bufR, frames, _smoothRatio, mix, levelGain);
        }
    }

    /// <summary>Polyphonic Octaver (-1 Oct + Dry + +1 Oct 동시 합성, RT 할당 0).</summary>
    public void ProcessOctaver(
        Span<float> bufL,
        Span<float> bufR,
        int frames,
        float dryPct,
        float subOctPct,
        float upOctPct,
        float toneKnob,
        float levelDb)
    {
        if (frames <= 0) return;

        float dryGain = Math.Clamp(dryPct, 0f, 100f) / 100f;
        float subGain = Math.Clamp(subOctPct, 0f, 100f) / 100f;
        float upGain = Math.Clamp(upOctPct, 0f, 100f) / 100f;
        float levelGain = MathF.Pow(10f, Math.Clamp(levelDb, -12f, 12f) / 20f);
        float cutoffHz = 1400f + Math.Clamp(toneKnob / 10f, 0f, 1f) * 10500f;
        float lpAlpha = 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / _sampleRate);

        bool isMono = IsMonoBlock(bufL, bufR, frames);
        ProcessChannelOctaver(_chL, bufL, frames, dryGain, subGain, upGain, lpAlpha, levelGain);
        if (isMono)
        {
            bufL.Slice(0, frames).CopyTo(bufR.Slice(0, frames));
            MirrorState(_chL, _chR);
        }
        else
        {
            ProcessChannelOctaver(_chR, bufR, frames, dryGain, subGain, upGain, lpAlpha, levelGain);
        }
    }

    private void ProcessChannelPitch(
        ChannelState st,
        Span<float> buf,
        int frames,
        float ratio,
        float mix,
        float levelGain)
    {
        float dryMix = 1f - mix;
        for (int i = 0; i < frames; i++)
        {
            float x = buf[i];
            st.InRing[st.InWrite] = x;
            st.InWrite = (st.InWrite + 1) & RingMask;

            float dryDelayed = st.DryRing[st.DryPos];
            st.DryRing[st.DryPos] = x;
            st.DryPos = (st.DryPos + 1) & RingMask;

            float wet = st.OutRing[st.OutPos];
            st.OutRing[st.OutPos] = 0f;
            st.OutPos = (st.OutPos + 1) & RingMask;

            buf[i] = (dryDelayed * dryMix + wet * mix) * levelGain;

            st.HopCount++;
            if (st.HopCount >= HopSize)
            {
                st.HopCount = 0;
                AnalyzeHop(st);
                SynthesizeSingleRatio(st, ratio);
                OverlapAddHop(st);
            }
        }
    }

    private void ProcessChannelOctaver(
        ChannelState st,
        Span<float> buf,
        int frames,
        float dryGain,
        float subGain,
        float upGain,
        float lpAlpha,
        float levelGain)
    {
        for (int i = 0; i < frames; i++)
        {
            float x = buf[i];
            st.InRing[st.InWrite] = x;
            st.InWrite = (st.InWrite + 1) & RingMask;

            float dryDelayed = st.DryRing[st.DryPos];
            st.DryRing[st.DryPos] = x;
            st.DryPos = (st.DryPos + 1) & RingMask;

            float wetRaw = st.OutRing[st.OutPos];
            st.OutRing[st.OutPos] = 0f;
            st.OutPos = (st.OutPos + 1) & RingMask;

            st.LpState += lpAlpha * (wetRaw - st.LpState);
            buf[i] = (dryDelayed * dryGain + st.LpState) * levelGain;

            st.HopCount++;
            if (st.HopCount >= HopSize)
            {
                st.HopCount = 0;
                AnalyzeHop(st);
                SynthesizeOctaver(st, subGain, upGain);
                OverlapAddHop(st);
            }
        }
    }

    private void AnalyzeHop(ChannelState st)
    {
        int readStart = (st.InWrite - FftSize) & RingMask;
        for (int i = 0; i < FftSize; i++)
        {
            st.FftRe[i] = st.InRing[(readStart + i) & RingMask] * HannWindow[i];
            st.FftIm[i] = 0f;
        }
        _fft.Forward(st.FftRe, st.FftIm);

        for (int k = 0; k < Bins; k++)
        {
            float re = st.FftRe[k];
            float im = st.FftIm[k];
            st.Mag[k] = MathF.Sqrt(re * re + im * im);
            float phase = MathF.Atan2(im, re);
            float dPhi = phase - st.LastPhase[k];
            st.LastPhase[k] = phase;

            float expected = k * FreqPerBin;
            dPhi = WrapPi(dPhi - expected);
            st.TrueFreq[k] = expected + dPhi;
        }
    }

    private static void SynthesizeSingleRatio(ChannelState st, float ratio)
    {
        Array.Clear(st.SynMagA);
        Array.Clear(st.SynFreqA);

        for (int k = 0; k < Bins; k++)
        {
            int dst = (int)MathF.Round(k * ratio);
            if ((uint)dst >= (uint)Bins) continue;
            float m = st.Mag[k];
            if (m > st.SynMagA[dst])
                st.SynFreqA[dst] = st.TrueFreq[k] * ratio;
            st.SynMagA[dst] += m;
        }

        for (int k = 0; k < Bins; k++)
        {
            float ph = WrapPi(st.SumPhaseA[k] + st.SynFreqA[k]);
            st.SumPhaseA[k] = ph;
            float mag = st.SynMagA[k];
            st.FftRe[k] = mag * MathF.Cos(ph);
            st.FftIm[k] = mag * MathF.Sin(ph);
        }
    }

    private static void SynthesizeOctaver(ChannelState st, float subGain, float upGain)
    {
        Array.Clear(st.SynMagA);
        Array.Clear(st.SynFreqA);
        Array.Clear(st.SynMagB);
        Array.Clear(st.SynFreqB);

        if (subGain > 1e-4f)
        {
            for (int k = 0; k < Bins; k++)
            {
                int dst = k >> 1; // ratio = 0.5
                float m = st.Mag[k];
                if (m > st.SynMagA[dst])
                    st.SynFreqA[dst] = st.TrueFreq[k] * 0.5f;
                st.SynMagA[dst] += m;
            }
        }

        if (upGain > 1e-4f)
        {
            for (int k = 0; k < Bins / 2; k++)
            {
                int dst = k << 1; // ratio = 2.0
                if (dst >= Bins) break;
                float m = st.Mag[k];
                if (m > st.SynMagB[dst])
                    st.SynFreqB[dst] = st.TrueFreq[k] * 2.0f;
                st.SynMagB[dst] += m;
            }
        }

        for (int k = 0; k < Bins; k++)
        {
            float phSub = WrapPi(st.SumPhaseA[k] + st.SynFreqA[k]);
            st.SumPhaseA[k] = phSub;
            float magSub = st.SynMagA[k] * subGain;

            float phUp = WrapPi(st.SumPhaseB[k] + st.SynFreqB[k]);
            st.SumPhaseB[k] = phUp;
            float magUp = st.SynMagB[k] * upGain;

            st.FftRe[k] = magSub * MathF.Cos(phSub) + magUp * MathF.Cos(phUp);
            st.FftIm[k] = magSub * MathF.Sin(phSub) + magUp * MathF.Sin(phUp);
        }
    }

    private void OverlapAddHop(ChannelState st)
    {
        st.FftIm[0] = 0f;
        st.FftIm[FftSize / 2] = 0f;
        for (int k = 1; k < FftSize / 2; k++)
        {
            st.FftRe[FftSize - k] = st.FftRe[k];
            st.FftIm[FftSize - k] = -st.FftIm[k];
        }

        _fft.Inverse(st.FftRe, st.FftIm);

        int outBase = st.OutPos;
        for (int i = 0; i < FftSize; i++)
            st.OutRing[(outBase + i) & RingMask] += st.FftRe[i] * HannWindow[i] * OlaNorm;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float WrapPi(float x)
    {
        const float twoPi = 2f * MathF.PI;
        x = (x + MathF.PI) % twoPi;
        if (x < 0f) x += twoPi;
        return x - MathF.PI;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMonoBlock(ReadOnlySpan<float> l, ReadOnlySpan<float> r, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (MathF.Abs(l[i] - r[i]) > 1e-6f)
                return false;
        }
        return true;
    }

    private static void MirrorState(ChannelState src, ChannelState dst)
    {
        Array.Copy(src.InRing, dst.InRing, FftSize);
        Array.Copy(src.OutRing, dst.OutRing, FftSize);
        Array.Copy(src.DryRing, dst.DryRing, FftSize);
        Array.Copy(src.LastPhase, dst.LastPhase, Bins);
        Array.Copy(src.SumPhaseA, dst.SumPhaseA, Bins);
        Array.Copy(src.SumPhaseB, dst.SumPhaseB, Bins);
        dst.InWrite = src.InWrite;
        dst.OutPos = src.OutPos;
        dst.DryPos = src.DryPos;
        dst.HopCount = src.HopCount;
        dst.LpState = src.LpState;
    }
}
