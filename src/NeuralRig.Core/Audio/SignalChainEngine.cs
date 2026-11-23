using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 실시간 Zero-Allocation 2행 × 8슬롯 모듈러 기타 시그널 체인 DSP 엔진.
/// - 유명 스톰프박스(TS808, Klon Centaur, ProCo RAT, Big Muff Pi, Boss SD-1, EP Boost, Dyna Comp, 1176)
/// - 화이트박스 4x HIIR 진공관 앰프(Plexi, JCM800 2203, US Twin, CA 1Star, SLO-100, 5153)
/// - 공식 .nam WaveNet Neural Capture 블록 및 외부/내장 Cab IR 블록
/// - 5-Band Graphic EQ / Parametric EQ
/// - 스테레오 CE-2 Chorus, Phase 90, Optical Tremolo
/// - 스테레오 Analog BBD / Digital Ping-Pong Delay
/// - 스테레오 Spring / Hall / Plate Reverb
/// - 직렬(Serial) 및 병렬 분기/합류(Parallel Split/Merge) 라우팅
/// </summary>
public sealed partial class SignalChainEngine
{
    public const int SlotCount = SignalChainConfigDto.SlotCount;

    internal enum BlockOp : byte
    {
        None = 0,
        GateAdaptive,
        CompDyna,
        Comp1176,
        CompBossCs3,
        CompSp,
        CompOrangeSqueezer,
        OdTs808,
        OdTs9,
        OdKlon,
        OdPureSky,
        OdJanRay,
        OdBbPreamp,
        OdOcd,
        OdBluesbreaker,
        DistRat,
        DistRat2,
        DistEcstasy,
        DistGuvnor,
        DistPlus,
        FuzzBigMuff,
        FuzzFace,
        OdSd1,
        OdBoxOfRock,
        BoostEp,
        BoostRc,
        BoostMicroAmp,
        BoostRangemaster,
        DistBossDs1,
        OdBossBd2,
        DistBossMt2,
        DistBossHm2,
        AmpWhiteBox,
        AmpJc120,
        NamCapture,
        CabIr,
        EqGraphic5,
        EqBossGe7,
        EqParametric,
        EqCryBaby,
        ModCe2,
        ModBossDc2w,
        ModBossBf2,
        ModSmallClone,
        ModUniVibe,
        ModPhase90,
        ModTremolo,
        DlyAnalog,
        DlyBossDm2w,
        DlyDigital,
        DlyBossDd3t,
        DlyTimeLine,
        RevSpring,
        RevHall,
        RevPlate,
        RevBlueSky,
        RevBossRv6,
    }

    internal struct CabMicParams
    {
        public bool DualEnabled;
        public byte MicAType;
        public float MicAX;
        public float MicADist;
        public float MicALevelGain;
        public float MicAPan;
        public bool MicAPhaseInv;

        public byte MicBType;
        public float MicBX;
        public float MicBDist;
        public float MicBLevelGain;
        public float MicBPan;
        public bool MicBPhaseInv;
        public float MicBDelayMs;
        public float Blend;
    }

    private struct CompiledSlot
    {
        public BlockOp Op;
        public byte SubModel; // Amp channel/voicing or Cab kind
        public byte SyncDiv;  // 0=Free, 1=1/4, 2=1/8., 3=1/8, 4=1/8T, 5=1/16
        public sbyte NamPoolIdx; // 0..2 for independent Multi-NAM, -1 if unassigned
        public bool Enabled;
        public float P0, P1, P2, P3, P4, P5, P6, P7;
        public CabMicParams CabMic;
    }

    private sealed class CompiledSnapshot
    {
        public readonly CompiledSlot[] Slots = new CompiledSlot[SlotCount];
        public readonly NamWorkspace?[] NamWorkspaces = new NamWorkspace?[3];
        public ChainRoutingMode Routing;
        public int SplitCol;
        public int MergeCol;
        public float ParallelMix;
        public float TempoBpm = 120f;
        public float InputGain;
        public float OutputGain;
    }

    // ── 공통 2차 RBJ 바이쿼드 (double 정밀도, 할당 0) ──
    private struct Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;

        public void SetBypass()
        {
            _b0 = 1.0; _b1 = 0.0; _b2 = 0.0; _a1 = 0.0; _a2 = 0.0;
        }

        public void SetLowPass(int fs, double fc, double q = 0.7071)
        {
            fc = Math.Clamp(fc, 20.0, fs * 0.45);
            double w0 = 2.0 * Math.PI * fc / fs;
            double cosW = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * Math.Max(0.1, q));
            double a0 = 1.0 + alpha;
            double inv = 1.0 / a0;
            _b0 = ((1.0 - cosW) * 0.5) * inv;
            _b1 = (1.0 - cosW) * inv;
            _b2 = _b0;
            _a1 = (-2.0 * cosW) * inv;
            _a2 = (1.0 - alpha) * inv;
        }

        public void SetHighPass(int fs, double fc, double q = 0.7071)
        {
            fc = Math.Clamp(fc, 15.0, fs * 0.45);
            double w0 = 2.0 * Math.PI * fc / fs;
            double cosW = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * Math.Max(0.1, q));
            double a0 = 1.0 + alpha;
            double inv = 1.0 / a0;
            _b0 = ((1.0 + cosW) * 0.5) * inv;
            _b1 = -(1.0 + cosW) * inv;
            _b2 = _b0;
            _a1 = (-2.0 * cosW) * inv;
            _a2 = (1.0 - alpha) * inv;
        }

        public void SetPeaking(int fs, double fc, double q, double gainDb)
        {
            if (Math.Abs(gainDb) < 0.02)
            {
                SetBypass();
                return;
            }
            fc = Math.Clamp(fc, 25.0, fs * 0.45);
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * fc / fs;
            double cosW = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * Math.Max(0.2, q));
            double a0 = 1.0 + alpha / A;
            double inv = 1.0 / a0;
            _b0 = (1.0 + alpha * A) * inv;
            _b1 = (-2.0 * cosW) * inv;
            _b2 = (1.0 - alpha * A) * inv;
            _a1 = _b1;
            _a2 = (1.0 - alpha / A) * inv;
        }

        public void SetLowShelf(int fs, double fc, double gainDb, double slope = 0.75)
        {
            if (Math.Abs(gainDb) < 0.02)
            {
                SetBypass();
                return;
            }
            fc = Math.Clamp(fc, 25.0, fs * 0.45);
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * fc / fs;
            double cosW = Math.Cos(w0);
            double sinW = Math.Sin(w0);
            double alpha = sinW * 0.5 * Math.Sqrt((A + 1.0 / A) * (1.0 / slope - 1.0) + 2.0);
            double twoSqrtA = 2.0 * Math.Sqrt(A) * alpha;
            double a0 = (A + 1.0) + (A - 1.0) * cosW + twoSqrtA;
            double inv = 1.0 / a0;
            _b0 = A * ((A + 1.0) - (A - 1.0) * cosW + twoSqrtA) * inv;
            _b1 = 2.0 * A * ((A - 1.0) - (A + 1.0) * cosW) * inv;
            _b2 = A * ((A + 1.0) - (A - 1.0) * cosW - twoSqrtA) * inv;
            _a1 = -2.0 * ((A - 1.0) + (A + 1.0) * cosW) * inv;
            _a2 = ((A + 1.0) + (A - 1.0) * cosW - twoSqrtA) * inv;
        }

        public void SetHighShelf(int fs, double fc, double gainDb, double slope = 0.75)
        {
            if (Math.Abs(gainDb) < 0.02)
            {
                SetBypass();
                return;
            }
            fc = Math.Clamp(fc, 200.0, fs * 0.45);
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * fc / fs;
            double cosW = Math.Cos(w0);
            double sinW = Math.Sin(w0);
            double alpha = sinW * 0.5 * Math.Sqrt((A + 1.0 / A) * (1.0 / slope - 1.0) + 2.0);
            double twoSqrtA = 2.0 * Math.Sqrt(A) * alpha;
            double a0 = (A + 1.0) - (A - 1.0) * cosW + twoSqrtA;
            double inv = 1.0 / a0;
            _b0 = A * ((A + 1.0) + (A - 1.0) * cosW + twoSqrtA) * inv;
            _b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cosW) * inv;
            _b2 = A * ((A + 1.0) + (A - 1.0) * cosW - twoSqrtA) * inv;
            _a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cosW) * inv;
            _a2 = ((A + 1.0) - (A - 1.0) * cosW - twoSqrtA) * inv;
        }

        public void Reset()
        {
            _z1 = _z2 = 0.0;
            SetBypass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyCoeffsFrom(in Biquad src)
        {
            _b0 = src._b0; _b1 = src._b1; _b2 = src._b2;
            _a1 = src._a1; _a2 = src._a2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input)
        {
            double x = input;
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return (float)y;
        }
    }

    // ── 슬롯별 사전 할당 DSP 상태 (RT 할당 0) ──
    private sealed class SlotDspState
    {
        // Gate / Comp
        public float Env;
        public float Gain = 1f;
        public bool GateOpen = true;
        public int HoldCount;

        // Pedal / EQ / Cab filters (L/R stereo capable, up to 7 bands for BOSS GE-7)
        public Biquad F0L, F1L, F2L, F3L, F4L, F5L, F6L;
        public Biquad F0R, F1R, F2R, F3R, F4R, F5R, F6R;

        // Modulation LFO & Allpass states
        public float LfoPhase;
        public float Ap0L, Ap1L, Ap2L, Ap3L;
        public float Ap0R, Ap1R, Ap2R, Ap3R;
        public float PhaserFbL, PhaserFbR;

        // Chorus small delay line (pre-allocated in Prepare)
        public float[] ChorusBufL = Array.Empty<float>();
        public float[] ChorusBufR = Array.Empty<float>();
        public int ChorusWrite;

        // 4x HIIR 오버샘플러 및 실제 회로도 아날로그 상태 (사전 할당, RT 할당 0)
        public readonly SchematicPedalDsp.SlotOversampler4x Os4x = new();
        public SchematicPedalDsp.CircuitState Circuit;

        // 지퍼 노이즈 방지 슬롯 출력 게인 스무딩
        public float SmoothOutGain = 1f;
        public bool SmoothReady;

        // 독립 리소스 풀 인덱스 (듀얼 앰프 / 듀얼 스테레오 캐비넷 / 딜레이 / 리버브)
        public BlockOp LastOp;
        public byte LastSubModel;
        public int AmpPoolIdx = -1;
        public int CabPoolIdx = -1;
        public int DelayPoolIdx = -1;
        public int ReverbPoolIdx = -1;

        public void Prepare(int sampleRate)
        {
            int chorusLen = Math.Max(2048, (int)(sampleRate * 0.05) + 64);
            // power-of-2 mask
            int cap = 1;
            while (cap < chorusLen) cap <<= 1;
            ChorusBufL = new float[cap];
            ChorusBufR = new float[cap];
            Reset();
        }

        public void Reset()
        {
            Env = 0f;
            Gain = 1f;
            GateOpen = true;
            HoldCount = 0;
            F0L.Reset(); F1L.Reset(); F2L.Reset(); F3L.Reset(); F4L.Reset(); F5L.Reset(); F6L.Reset();
            F0R.Reset(); F1R.Reset(); F2R.Reset(); F3R.Reset(); F4R.Reset(); F5R.Reset(); F6R.Reset();
            LfoPhase = 0f;
            Ap0L = Ap1L = Ap2L = Ap3L = 0f;
            Ap0R = Ap1R = Ap2R = Ap3R = 0f;
            PhaserFbL = PhaserFbR = 0f;
            if (ChorusBufL.Length > 0) Array.Clear(ChorusBufL);
            if (ChorusBufR.Length > 0) Array.Clear(ChorusBufR);
            ChorusWrite = 0;
            Os4x.Reset();
            Circuit.Reset();
            SmoothReady = false;
        }
    }

    // ── 스테레오 딜레이 라인 풀 (최대 2개 딜레이 블록 동시 지원, 사전 할당) ──
    private sealed class StereoDelayLine
    {
        public float[] BufL = Array.Empty<float>();
        public float[] BufR = Array.Empty<float>();
        public int WritePos;
        public float LpL, LpR;
        public float ModPhase;
        public float SmoothDelayL;
        public float SmoothDelayR;
        public bool DelayReady;

        public void Prepare(int sampleRate)
        {
            int maxSamples = Math.Max(65536, (int)(sampleRate * 1.6));
            BufL = new float[maxSamples];
            BufR = new float[maxSamples];
            Reset();
        }

        public void Reset()
        {
            if (BufL.Length > 0) Array.Clear(BufL);
            if (BufR.Length > 0) Array.Clear(BufR);
            WritePos = 0;
            LpL = LpR = 0f;
            ModPhase = 0f;
            SmoothDelayL = SmoothDelayR = 0f;
            DelayReady = false;
        }
    }

    // ── 스테레오 Freeverb/Tank 리버브 풀 (최대 2개 리버브 블록 동시 지원, 사전 할당) ──
    private sealed class StereoReverbTank
    {
        private static readonly int[] CombTunings = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllpassTunings = { 556, 441, 341, 225 };
        private const int StereoSpread = 23;

        private float[][] _combL = Array.Empty<float[]>();
        private float[][] _combR = Array.Empty<float[]>();
        private int[] _combIdxL = new int[8];
        private int[] _combIdxR = new int[8];
        private float[] _combFiltL = new float[8];
        private float[] _combFiltR = new float[8];

        private float[][] _apL = Array.Empty<float[]>();
        private float[][] _apR = Array.Empty<float[]>();
        private int[] _apIdxL = new int[4];
        private int[] _apIdxR = new int[4];

        private float[] _preDelayL = Array.Empty<float>();
        private float[] _preDelayR = Array.Empty<float>();
        private int _preIdx;

        // Strymon blueSky +1 octave shimmer grain buffer (pre-allocated, 0 RT alloc)
        private const int ShimLen = 2048;
        private const int ShimMask = ShimLen - 1;
        private float[] _shimBuf = Array.Empty<float>();
        private int _shimWrite;
        private float _shimPhase;

        public void Prepare(int sampleRate)
        {
            double scale = sampleRate / 44100.0;
            _combL = new float[8][];
            _combR = new float[8][];
            for (int i = 0; i < 8; i++)
            {
                int nL = Math.Max(64, (int)Math.Round(CombTunings[i] * scale));
                int nR = Math.Max(64, (int)Math.Round((CombTunings[i] + StereoSpread) * scale));
                _combL[i] = new float[nL];
                _combR[i] = new float[nR];
            }
            _apL = new float[4][];
            _apR = new float[4][];
            for (int i = 0; i < 4; i++)
            {
                int nL = Math.Max(32, (int)Math.Round(AllpassTunings[i] * scale));
                int nR = Math.Max(32, (int)Math.Round((AllpassTunings[i] + StereoSpread) * scale));
                _apL[i] = new float[nL];
                _apR[i] = new float[nR];
            }
            int preMax = Math.Max(2048, (int)(sampleRate * 0.16) + 16);
            _preDelayL = new float[preMax];
            _preDelayR = new float[preMax];
            _shimBuf = new float[ShimLen];
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < _combL.Length; i++)
            {
                Array.Clear(_combL[i]);
                Array.Clear(_combR[i]);
                _combIdxL[i] = _combIdxR[i] = 0;
                _combFiltL[i] = _combFiltR[i] = 0f;
            }
            for (int i = 0; i < _apL.Length; i++)
            {
                Array.Clear(_apL[i]);
                Array.Clear(_apR[i]);
                _apIdxL[i] = _apIdxR[i] = 0;
            }
            if (_preDelayL.Length > 0) Array.Clear(_preDelayL);
            if (_preDelayR.Length > 0) Array.Clear(_preDelayR);
            if (_shimBuf.Length > 0) Array.Clear(_shimBuf);
            _preIdx = 0;
            _shimWrite = 0;
            _shimPhase = 0f;
        }

        public void Process(Span<float> bufL, Span<float> bufR, int frames, int sr, float decayPct, float dampPct, float preDelayMs, float mixPct, bool isSpring, bool isPlate, float shimmerAmt = 0f)
        {
            if (_combL.Length == 0) return;

            float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
            float dry = 1f - mix * 0.65f;
            float feedback = isSpring
                ? 0.58f + 0.30f * Math.Clamp(decayPct / 100f, 0f, 1f)
                : 0.68f + 0.27f * Math.Clamp(decayPct / 100f, 0f, 1f);
            float damp1 = isPlate
                ? 0.08f + 0.38f * Math.Clamp(dampPct / 100f, 0f, 1f)
                : 0.12f + 0.58f * Math.Clamp(dampPct / 100f, 0f, 1f);
            float damp2 = 1f - damp1;
            int preSamps = Math.Clamp((int)(preDelayMs * 0.001f * sr), 0, _preDelayL.Length - 2);
            int numCombs = isSpring ? 4 : 8;
            float wetGain = isSpring ? 0.028f : 0.018f;
            bool useShimmer = shimmerAmt > 0.01f && _shimBuf.Length == ShimLen;
            float shimMix = Math.Clamp(shimmerAmt, 0f, 1f) * 0.42f;

            for (int i = 0; i < frames; i++)
            {
                float inL = bufL[i];
                float inR = bufR[i];

                // Pre-delay
                _preDelayL[_preIdx] = inL;
                _preDelayR[_preIdx] = inR;
                int rdPre = _preIdx - preSamps;
                if (rdPre < 0) rdPre += _preDelayL.Length;
                float xL = _preDelayL[rdPre] * wetGain;
                float xR = _preDelayR[rdPre] * wetGain;
                _preIdx++;
                if (_preIdx >= _preDelayL.Length) _preIdx = 0;

                float monoIn = (xL + xR) * 0.5f;

                if (useShimmer)
                {
                    // Dual-tap crossfaded 2x-speed granular pitch shifter (+1 octave shimmer)
                    float p1 = _shimPhase;
                    float p2 = p1 + 0.5f;
                    if (p2 >= 1f) p2 -= 1f;
                    float w1 = 1f - MathF.Abs(2f * p1 - 1f);
                    float w2 = 1f - MathF.Abs(2f * p2 - 1f);
                    float d1 = p1 * 1024f;
                    float d2 = p2 * 1024f;
                    float s1 = ReadFrac(_shimBuf, _shimWrite, d1, ShimMask);
                    float s2 = ReadFrac(_shimBuf, _shimWrite, d2, ShimMask);
                    float octUp = s1 * w1 + s2 * w2;
                    monoIn += octUp * shimMix;

                    _shimPhase -= 1f / 1024f;
                    if (_shimPhase < 0f) _shimPhase += 1f;
                }

                float accL = 0f, accR = 0f;

                for (int c = 0; c < numCombs; c++)
                {
                    var cL = _combL[c];
                    int idxL = _combIdxL[c];
                    float outL = cL[idxL];
                    _combFiltL[c] = outL * damp2 + _combFiltL[c] * damp1;
                    cL[idxL] = monoIn + _combFiltL[c] * feedback;
                    if (++idxL >= cL.Length) idxL = 0;
                    _combIdxL[c] = idxL;
                    accL += outL;

                    var cR = _combR[c];
                    int idxR = _combIdxR[c];
                    float outR = cR[idxR];
                    _combFiltR[c] = outR * damp2 + _combFiltR[c] * damp1;
                    cR[idxR] = monoIn + _combFiltR[c] * feedback;
                    if (++idxR >= cR.Length) idxR = 0;
                    _combIdxR[c] = idxR;
                    accR += outR;
                }

                for (int a = 0; a < 4; a++)
                {
                    var aL = _apL[a];
                    int iL = _apIdxL[a];
                    float bL = aL[iL];
                    float yL = -accL + bL;
                    aL[iL] = accL + bL * 0.5f;
                    if (++iL >= aL.Length) iL = 0;
                    _apIdxL[a] = iL;
                    accL = yL;

                    var aR = _apR[a];
                    int iR = _apIdxR[a];
                    float bR = aR[iR];
                    float yR = -accR + bR;
                    aR[iR] = accR + bR * 0.5f;
                    if (++iR >= aR.Length) iR = 0;
                    _apIdxR[a] = iR;
                    accR = yR;
                }

                if (useShimmer)
                {
                    _shimBuf[_shimWrite] = (accL + accR) * 0.015f;
                    _shimWrite = (_shimWrite + 1) & ShimMask;
                }

                bufL[i] = inL * dry + accL * mix;
                bufR[i] = inR * dry + accR * mix;
            }
        }
    }

    private sealed class CabSlotUnit
    {
        public readonly StereoPartitionedConvolver Convolver = new();
        public Biquad A0L, A1L, A2L, A0R, A1R, A2R;
        public Biquad B0L, B1L, B2L, B0R, B1R, B2R;
        public float[] HaasBufL = Array.Empty<float>();
        public float[] HaasBufR = Array.Empty<float>();
        public int HaasWrite;
        public float[] MicAL = Array.Empty<float>();
        public float[] MicAR = Array.Empty<float>();
        public float[] MicBL = Array.Empty<float>();
        public float[] MicBR = Array.Empty<float>();

        public void Prepare(int maxFrames)
        {
            Convolver.Prepare(maxFrames);
            HaasBufL = new float[2048];
            HaasBufR = new float[2048];
            MicAL = new float[maxFrames];
            MicAR = new float[maxFrames];
            MicBL = new float[maxFrames];
            MicBR = new float[maxFrames];
            Reset();
        }

        public void Reset()
        {
            Convolver.Reset();
            A0L.Reset(); A1L.Reset(); A2L.Reset(); A0R.Reset(); A1R.Reset(); A2R.Reset();
            B0L.Reset(); B1L.Reset(); B2L.Reset(); B0R.Reset(); B1R.Reset(); B2R.Reset();
            if (HaasBufL.Length > 0) Array.Clear(HaasBufL);
            if (HaasBufR.Length > 0) Array.Clear(HaasBufR);
            HaasWrite = 0;
        }
    }

    // ── 엔진 필드 (모든 풀 사전 할당, RT 할당 0) ──
    private readonly SlotDspState[] _states = new SlotDspState[SlotCount];
    private readonly WhiteBoxAmpEngine[] _ampPool = { new(), new() };
    private readonly CabinetIrBank _cabBank = new();
    private readonly CabSlotUnit[] _cabPool = { new(), new() };
    private readonly StereoDelayLine[] _delayPool = { new(), new() };
    private readonly StereoReverbTank[] _reverbPool = { new(), new() };

    private CompiledSnapshot _pendingSnapshot = new();
    private CompiledSnapshot _activeSnapshot = new();
    private SignalChainConfigDto _lastDto = new();
    private Func<string?, ToneProfile?>? _namResolver;
    private readonly (string? modelId, ToneProfile? profile, NamWorkspace? ws)[] _uiNamPool = new (string?, ToneProfile?, NamWorkspace?)[3];
    private readonly object _chainCompileLock = new();
    private int _version = 1;
    private int _appliedVersion;

    // 전역 입출력 및 병렬 믹스 스무딩 (지퍼 노이즈 제거)
    private float _smoothInGain = 1f;
    private float _smoothOutGain = 1f;
    private float _smoothParMix = 0.5f;
    private bool _smoothReady;

    private int _sampleRate = 44100;
    private int _maxFrames = 2048;

    private float[] _parL = Array.Empty<float>();
    private float[] _parR = Array.Empty<float>();
    private float[] _monoTmp = Array.Empty<float>();
    private double[] _os2x = Array.Empty<double>();
    private float[] _os4x = Array.Empty<float>();

    public SignalChainEngine()
    {
        for (int i = 0; i < SlotCount; i++)
            _states[i] = new SlotDspState();
        UpdateChain(new SignalChainConfigDto());
    }

    public SignalChainConfigDto GetConfigSnapshot() => Volatile.Read(ref _lastDto).Clone();

    internal bool IsMeasuredCabIr(int subModel) => _cabBank.IsMeasuredIr(subModel);

    public float[]? GetEffectiveCabIr(float[]? customIr, float[]? profileIr)
    {
        var snap = Volatile.Read(ref _pendingSnapshot);
        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            if (sl.Op == BlockOp.CabIr && sl.Enabled)
            {
                if (sl.SubModel == 1 && customIr is { Length: > 0 }) return customIr;
                if (sl.SubModel == 1 && profileIr is { Length: > 0 }) return profileIr;
                var bankIr = _cabBank.GetIr(sl.SubModel);
                if (bankIr.Length > 0) return bankIr;
            }
        }
        return null;
    }

    public void Prepare(int maxFrames, int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _maxFrames = Math.Max(256, maxFrames);
        _parL = new float[_maxFrames];
        _parR = new float[_maxFrames];
        _monoTmp = new float[_maxFrames];
        _os2x = new double[_maxFrames * 2];
        _os4x = new float[_maxFrames * 4];

        _cabBank.Prepare(_sampleRate);
        for (int i = 0; i < _ampPool.Length; i++)
            _ampPool[i].Prepare(_maxFrames, _sampleRate);
        for (int i = 0; i < _cabPool.Length; i++)
            _cabPool[i].Prepare(_maxFrames);
        for (int i = 0; i < SlotCount; i++)
            _states[i].Prepare(_sampleRate);
        for (int i = 0; i < _delayPool.Length; i++)
            _delayPool[i].Prepare(_sampleRate);
        for (int i = 0; i < _reverbPool.Length; i++)
            _reverbPool[i].Prepare(_sampleRate);

        _smoothReady = false;
        _appliedVersion = 0;
        EnsureApplied();
    }

    public void Reset()
    {
        for (int i = 0; i < _ampPool.Length; i++) _ampPool[i].Reset();
        for (int i = 0; i < _cabPool.Length; i++) _cabPool[i].Reset();
        for (int i = 0; i < SlotCount; i++)
        {
            _states[i].Reset();
            _states[i].LastOp = BlockOp.None;
            _states[i].LastSubModel = 0;
        }
        for (int i = 0; i < _delayPool.Length; i++) _delayPool[i].Reset();
        for (int i = 0; i < _reverbPool.Length; i++) _reverbPool[i].Reset();
        var act = _activeSnapshot;
        for (int i = 0; i < act.NamWorkspaces.Length; i++) act.NamWorkspaces[i]?.Reset();
        _smoothReady = false;
        _appliedVersion = 0;
    }

    public void SetNamModelResolver(Func<string?, ToneProfile?>? resolver)
    {
        _namResolver = resolver;
    }

    public void RefreshNamBindings()
    {
        var dto = Volatile.Read(ref _lastDto);
        if (dto != null)
            UpdateChain(dto);
    }

    /// <summary>UI 스레드에서 호출: 체인 구성을 컴파일하여 RT 스레드에 원자적으로 전달한다.</summary>
    public void UpdateChain(SignalChainConfigDto dto, Func<string?, ToneProfile?>? namResolver = null)
    {
        lock (_chainCompileLock)
        {
            var resolver = namResolver ?? _namResolver;
            var clean = (dto ?? new SignalChainConfigDto()).Clone();
            var snap = new CompiledSnapshot
            {
                Routing = (ChainRoutingMode)Math.Clamp(clean.RoutingMode, 0, 1),
                SplitCol = Math.Clamp(clean.SplitCol, 0, 7),
                MergeCol = Math.Clamp(clean.MergeCol, 0, 7),
                ParallelMix = Math.Clamp(clean.ParallelMix, 0f, 1f),
                TempoBpm = Math.Clamp(clean.TempoBpm > 1f ? clean.TempoBpm : 120f, 40f, 280f),
                InputGain = MathF.Pow(10f, Math.Clamp(clean.InputGainDb, -18f, 18f) / 20f),
                OutputGain = MathF.Pow(10f, Math.Clamp(clean.OutputGainDb, -18f, 18f) / 20f),
            };

            int namIdx = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var s = clean.Slots[i];
                var (op, sub) = ParseModelId(s.ModelId);
                sbyte assignedNam = -1;
                if (op == BlockOp.NamCapture && namIdx < snap.NamWorkspaces.Length)
                {
                    assignedNam = (sbyte)namIdx;
                    var prof = resolver?.Invoke(s.NamModelId);
                    if (prof != null && !prof.HasNeuralNet && prof.Weights.Length > 0 &&
                        (prof.WaveNetConfig != null || prof.LstmConfig != null || prof.ConvNetConfig != null))
                    {
                        prof.PrepareForRt();
                    }

                    if (prof != null && (prof.IsWaveNet || prof.IsLstm || prof.IsConvNet))
                    {
                        var cached = _uiNamPool[namIdx];
                        if (ReferenceEquals(cached.profile, prof) && cached.ws != null)
                        {
                            snap.NamWorkspaces[namIdx] = cached.ws;
                        }
                        else
                        {
                            var ws = new NamWorkspace(prof);
                            _uiNamPool[namIdx] = (s.NamModelId, prof, ws);
                            snap.NamWorkspaces[namIdx] = ws;
                        }
                    }
                    else
                    {
                        _uiNamPool[namIdx] = (s.NamModelId, null, null);
                    }
                    namIdx++;
                }

                var p = s.Params;
                var cm = s.CabMic;
                var micParams = cm != null && cm.DualEnabled
                    ? new CabMicParams
                    {
                        DualEnabled = true,
                        MicAType = (byte)Math.Clamp(cm.MicAType, 0, 5),
                        MicAX = Math.Clamp(cm.MicAX, 0f, 1f),
                        MicADist = Math.Clamp(cm.MicADist, 0f, 1f),
                        MicALevelGain = MathF.Pow(10f, Math.Clamp(cm.MicALevelDb, -18f, 6f) / 20f),
                        MicAPan = Math.Clamp(cm.MicAPan, -1f, 1f),
                        MicAPhaseInv = cm.MicAPhaseInv,
                        MicBType = (byte)Math.Clamp(cm.MicBType, 0, 5),
                        MicBX = Math.Clamp(cm.MicBX, 0f, 1f),
                        MicBDist = Math.Clamp(cm.MicBDist, 0f, 1f),
                        MicBLevelGain = MathF.Pow(10f, Math.Clamp(cm.MicBLevelDb, -18f, 6f) / 20f),
                        MicBPan = Math.Clamp(cm.MicBPan, -1f, 1f),
                        MicBPhaseInv = cm.MicBPhaseInv,
                        MicBDelayMs = Math.Clamp(cm.MicBDelayMs, 0f, 15f),
                        Blend = Math.Clamp(cm.Blend, 0f, 1f),
                    }
                    : default;

                snap.Slots[i] = new CompiledSlot
                {
                    Op = op,
                    SubModel = sub,
                    SyncDiv = (byte)Math.Clamp(s.SyncDivision, 0, 5),
                    NamPoolIdx = assignedNam,
                    Enabled = s.Enabled,
                    P0 = p[0], P1 = p[1], P2 = p[2], P3 = p[3],
                    P4 = p[4], P5 = p[5], P6 = p[6], P7 = p[7],
                    CabMic = micParams,
                };
            }

            Volatile.Write(ref _lastDto, clean);
            Volatile.Write(ref _pendingSnapshot, snap);
            Interlocked.Increment(ref _version);
        }
    }

    private static (BlockOp op, byte sub) ParseModelId(string? id)
    {
        return (id ?? "").Trim().ToLowerInvariant() switch
        {
            "gate_adaptive" => (BlockOp.GateAdaptive, 0),
            "comp_dyna" => (BlockOp.CompDyna, 0),
            "comp_1176" => (BlockOp.Comp1176, 0),
            "comp_cs3" => (BlockOp.CompBossCs3, 0),
            "comp_sp" => (BlockOp.CompSp, 0),
            "comp_orangesqueezer" => (BlockOp.CompOrangeSqueezer, 0),
            "od_ts808" => (BlockOp.OdTs808, 0),
            "od_ts9" => (BlockOp.OdTs9, 0),
            "od_klon" => (BlockOp.OdKlon, 0),
            "od_puresky" => (BlockOp.OdPureSky, 0),
            "od_janray" => (BlockOp.OdJanRay, 0),
            "od_bbpreamp" => (BlockOp.OdBbPreamp, 0),
            "od_ocd" => (BlockOp.OdOcd, 0),
            "od_bluesbreaker" => (BlockOp.OdBluesbreaker, 0),
            "dist_rat" => (BlockOp.DistRat, 0),
            "dist_rat2" => (BlockOp.DistRat2, 0),
            "dist_ecstasy" => (BlockOp.DistEcstasy, 0),
            "dist_guvnor" => (BlockOp.DistGuvnor, 0),
            "dist_plus" => (BlockOp.DistPlus, 0),
            "fuzz_bigmuff" => (BlockOp.FuzzBigMuff, 0),
            "fuzz_face" => (BlockOp.FuzzFace, 0),
            "od_sd1" => (BlockOp.OdSd1, 0),
            "od_boxofrock" => (BlockOp.OdBoxOfRock, 0),
            "boost_ep" => (BlockOp.BoostEp, 0),
            "boost_rc" => (BlockOp.BoostRc, 0),
            "boost_microamp" => (BlockOp.BoostMicroAmp, 0),
            "boost_rangemaster" => (BlockOp.BoostRangemaster, 0),
            "dist_ds1" => (BlockOp.DistBossDs1, 0),
            "od_bd2" => (BlockOp.OdBossBd2, 0),
            "dist_mt2" => (BlockOp.DistBossMt2, 0),
            "dist_hm2" => (BlockOp.DistBossHm2, 0),
            "amp_twin" => (BlockOp.AmpWhiteBox, 0),   // US Clean Twin
            "amp_1star" => (BlockOp.AmpWhiteBox, 1),  // CA 1Star Clean
            "amp_plexi" => (BlockOp.AmpWhiteBox, 2),  // Brit Plexi 100 Bright
            "amp_2203" => (BlockOp.AmpWhiteBox, 3),   // Brit 2203
            "amp_slo100" => (BlockOp.AmpWhiteBox, 4), // SLO 100 Lead
            "amp_5153" => (BlockOp.AmpWhiteBox, 5),   // 5153 Red High-Gain
            "amp_ac30" => (BlockOp.AmpWhiteBox, 6),   // VOX AC30 Top Boost (J-Pop/J-Rock)
            "amp_dsl" => (BlockOp.AmpWhiteBox, 7),    // Marshall JCM2000 DSL (J-Rock Live House)
            "amp_recto" => (BlockOp.AmpWhiteBox, 8),  // Mesa Dual Rectifier (Modern J-Rock/Anisong)
            "amp_be100" => (BlockOp.AmpWhiteBox, 9),  // Friedman BE-100 (Boutique J-Rock)
            "amp_jc120" => (BlockOp.AmpJc120, 0),     // Roland JC-120 Jazz Chorus (Japanese Studio Clean)
            "nam_capture" => (BlockOp.NamCapture, 0),
            "cab_v30" => (BlockOp.CabIr, 0),
            "cab_custom" => (BlockOp.CabIr, 1),
            "cab_jc120" => (BlockOp.CabIr, 2),        // 2x12 JC-120 Open Back
            "cab_alnico" => (BlockOp.CabIr, 3),       // 2x12 VOX Alnico Blue
            "cab_1960a" => (BlockOp.CabIr, 4),        // Marshall 1960A G12T-75
            "cab_1960bv" => (BlockOp.CabIr, 7),       // Marshall 1960BV 4x12 V30 Straight
            "cab_mesa412" => (BlockOp.CabIr, 5),      // Mesa/Boogie Rectifier 4x12
            "cab_jensen112" => (BlockOp.CabIr, 6),    // 1x12 Fender Jensen Open
            "eq_graphic5" => (BlockOp.EqGraphic5, 0),
            "eq_ge7" => (BlockOp.EqBossGe7, 0),
            "eq_parametric" => (BlockOp.EqParametric, 0),
            "eq_crybaby" => (BlockOp.EqCryBaby, 0),
            "mod_ce2" => (BlockOp.ModCe2, 0),
            "mod_dc2w" => (BlockOp.ModBossDc2w, 0),
            "mod_bf2" => (BlockOp.ModBossBf2, 0),
            "mod_smallclone" => (BlockOp.ModSmallClone, 0),
            "mod_univibe" => (BlockOp.ModUniVibe, 0),
            "mod_phase90" => (BlockOp.ModPhase90, 0),
            "mod_tremolo" => (BlockOp.ModTremolo, 0),
            "dly_analog" => (BlockOp.DlyAnalog, 0),
            "dly_dm2w" => (BlockOp.DlyBossDm2w, 0),
            "dly_digital" => (BlockOp.DlyDigital, 0),
            "dly_dd3t" => (BlockOp.DlyBossDd3t, 0),
            "dly_timeline" => (BlockOp.DlyTimeLine, 0),
            "rev_spring" => (BlockOp.RevSpring, 0),
            "rev_hall" => (BlockOp.RevHall, 0),
            "rev_plate" => (BlockOp.RevPlate, 0),
            "rev_bluesky" => (BlockOp.RevBlueSky, 0),
            "rev_rv6" => (BlockOp.RevBossRv6, 0),
            _ => (BlockOp.None, 0),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureApplied()
    {
        int ver = Volatile.Read(ref _version);
        if (ver == _appliedVersion) return;

        var snap = Volatile.Read(ref _pendingSnapshot);
        _activeSnapshot = snap;

        int ampIdx = 0;
        int cabIdx = 0;
        int dlyIdx = 0;
        int revIdx = 0;
        int sr = _sampleRate;

        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            var st = _states[i];
            int prevAmpIdx = st.AmpPoolIdx;
            int prevCabIdx = st.CabPoolIdx;
            int prevDlyIdx = st.DelayPoolIdx;
            int prevRevIdx = st.ReverbPoolIdx;
            bool modelChanged = st.LastOp != sl.Op || st.LastSubModel != sl.SubModel;
            if (modelChanged)
            {
                st.Reset();
                st.LastOp = sl.Op;
                st.LastSubModel = sl.SubModel;
            }
            st.AmpPoolIdx = -1;
            st.CabPoolIdx = -1;
            st.DelayPoolIdx = -1;
            st.ReverbPoolIdx = -1;

            switch (sl.Op)
            {
                case BlockOp.OdTs808:
                {
                    // TS808 액티브 톤 회로: 720Hz..3.3kHz 패시브 LPF + 2.4kHz 액티브 하이셸프 부스트 (Tone > 5)
                    double tNorm = Math.Clamp(sl.P1 / 10.0, 0.0, 1.0);
                    st.F0L.SetHighPass(sr, 720.0, 0.707);
                    st.F1L.SetLowPass(sr, 720.0 + 2800.0 * tNorm, 0.707);
                    st.F2L.SetHighShelf(sr, 2400.0, (tNorm - 0.45) * 9.5, 0.75);
                    break;
                }
                case BlockOp.OdTs9:
                {
                    // Ibanez TS9: 약간 더 타이트하고 어퍼미드가 살아있는 액티브 톤
                    double tNorm = Math.Clamp(sl.P1 / 10.0, 0.0, 1.0);
                    st.F0L.SetHighPass(sr, 760.0, 0.72);
                    st.F1L.SetLowPass(sr, 900.0 + 3200.0 * tNorm, 0.707);
                    st.F2L.SetHighShelf(sr, 2500.0, (tNorm - 0.40) * 10.5, 0.75);
                    break;
                }
                case BlockOp.OdPureSky:
                    // Caline Pure Sky (Timmy-style transparent OD with pre-clip Bass & post-clip Treble)
                    st.F0L.SetHighPass(sr, 45.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 18.0, 0.707);
                    st.F1L.SetHighShelf(sr, 2600.0, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 1.9, 0.72);
                    st.F2L.SetLowPass(sr, 6500.0 + Math.Clamp(sl.P2, 0f, 10f) * 450.0, 0.707);
                    break;
                case BlockOp.OdJanRay:
                    // Vemuram Jan Ray: Blackface Fender-in-a-box, sparkling treble & warm tight bass
                    st.F0L.SetHighPass(sr, 60.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 14.0, 0.707);
                    st.F1L.SetPeaking(sr, 130.0, 0.85, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.4);
                    st.F2L.SetHighShelf(sr, 3000.0, (Math.Clamp(sl.P2, 0f, 10f) - 4.5) * 1.8, 0.75);
                    st.F3L.SetLowPass(sr, 8500.0, 0.707);
                    break;
                case BlockOp.OdBbPreamp:
                    // Xotic BB Preamp: smooth singing J-Rock/Fusion OD with active +/-15dB Bass & Treble
                    st.F0L.SetHighPass(sr, 90.0, 0.707);
                    st.F1L.SetPeaking(sr, 115.0, 0.85, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 2.2);
                    st.F2L.SetHighShelf(sr, 2800.0, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.2, 0.72);
                    st.F3L.SetLowPass(sr, 6200.0, 0.707);
                    break;
                case BlockOp.OdOcd:
                {
                    // Fulltone OCD: MOSFET clipping with HP (Marshall-ish mid/treble push) vs LP (flat)
                    bool hp = sl.P3 > 0.5f;
                    st.F0L.SetHighPass(sr, hp ? 130.0 : 80.0, 0.707);
                    st.F1L.SetPeaking(sr, 1400.0, 0.85, hp ? 3.2 : 0.4);
                    st.F2L.SetLowPass(sr, 1400.0 + Math.Clamp(sl.P1, 0f, 10f) * 520.0, 0.72);
                    break;
                }
                case BlockOp.DistRat2:
                {
                    // ProCo RAT 2: tighter OP07DP / LM308 punchy hard-clip + iconic reverse Filter
                    st.F0L.SetHighPass(sr, 165.0, 0.75);
                    double fc = 1050.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 820.0;
                    st.F1L.SetLowPass(sr, fc, 0.72);
                    st.F2L.SetPeaking(sr, 1300.0, 1.0, 2.0);
                    break;
                }
                case BlockOp.DistEcstasy:
                {
                    // Bogner Ecstasy Red: high-gain J-Rock/Anisong lead pedal with 3-band EQ
                    st.F0L.SetHighPass(sr, 145.0, 0.707);
                    st.F1L.SetPeaking(sr, 110.0, 0.9, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 2.0);
                    st.F2L.SetPeaking(sr, 750.0, 1.0, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.2);
                    st.F3L.SetHighShelf(sr, 3200.0, (Math.Clamp(sl.P3, 0f, 10f) - 5.0) * 2.0, 0.75);
                    st.F4L.SetLowPass(sr, 7400.0, 0.707);
                    break;
                }
                case BlockOp.BoostMicroAmp:
                    st.F0L.SetHighShelf(sr, 3800.0, 1.4, 0.75);
                    break;
                case BlockOp.BoostRc:
                    // Xotic RC Booster: active +/-15dB Bass & Treble clean boost
                    st.F0L.SetPeaking(sr, 110.0, 0.85, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.4);
                    st.F1L.SetHighShelf(sr, 3200.0, (Math.Clamp(sl.P3, 0f, 10f) - 5.0) * 2.4, 0.75);
                    break;
                case BlockOp.AmpJc120:
                {
                    // Roland JC-120 Jazz Chorus: 진정한 스테레오 L/R 솔리드스테이트 클린 앰프
                    double bG = (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 2.0;
                    double mG = (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.0;
                    double tG = (Math.Clamp(sl.P3, 0f, 10f) - 5.0) * 2.2 + (sl.P7 > 0.5f ? 3.8 : 0.0);
                    st.F0L.SetPeaking(sr, 110.0, 0.85, bG);
                    st.F1L.SetPeaking(sr, 650.0, 0.9, mG);
                    st.F2L.SetHighShelf(sr, 2800.0, tG, 0.72);
                    st.F3L.SetHighPass(sr, 55.0, 0.707);
                    st.F0R.SetPeaking(sr, 110.0, 0.85, bG);
                    st.F1R.SetPeaking(sr, 650.0, 0.9, mG);
                    st.F2R.SetHighShelf(sr, 2800.0, tG, 0.72);
                    st.F3R.SetHighPass(sr, 55.0, 0.707);
                    break;
                }
                case BlockOp.AmpWhiteBox:
                {
                    // 독립 듀얼-앰프 풀 배정 (Row 1 + Row 2 병렬 듀얼 앰프 간 상태 충돌 완전 차단)
                    st.AmpPoolIdx = (ampIdx++) % _ampPool.Length;
                    if (modelChanged || st.AmpPoolIdx != prevAmpIdx)
                        _ampPool[st.AmpPoolIdx].Reset();
                    // Per-model amp voicing contour (AC30 chime, DSL punch, Recto tight low, BE-100 growl)
                    switch (sl.SubModel)
                    {
                        case 6: // VOX AC30 Top Boost
                            st.F0L.SetPeaking(sr, 1950.0, 0.9, 3.2);
                            st.F1L.SetHighShelf(sr, 3600.0, 2.4, 0.75);
                            break;
                        case 7: // Marshall JCM2000 DSL
                            st.F0L.SetPeaking(sr, 950.0, 0.95, 2.0);
                            st.F1L.SetHighShelf(sr, 3200.0, 1.5, 0.75);
                            break;
                        case 8: // Mesa Dual Rectifier
                            st.F0L.SetHighPass(sr, 95.0, 0.707);
                            st.F1L.SetPeaking(sr, 520.0, 0.9, -2.8);
                            break;
                        case 9: // Friedman BE-100
                            st.F0L.SetPeaking(sr, 820.0, 0.95, 2.8);
                            st.F1L.SetHighShelf(sr, 4200.0, 1.2, 0.75);
                            break;
                        default:
                            st.F0L.SetBypass();
                            st.F1L.SetBypass();
                            break;
                    }
                    break;
                }
                case BlockOp.OdKlon:
                    st.F1L.SetPeaking(sr, 1000.0, 0.9, 1.5 + 3.0 * Math.Clamp(sl.P0 / 10.0, 0.0, 1.0));
                    st.F2L.SetHighShelf(sr, 2200.0, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.8, 0.75);
                    break;
                case BlockOp.DistRat:
                {
                    st.F0L.SetHighPass(sr, 140.0, 0.707);
                    // RAT Filter knob: higher value rolls off more treble (or standard tone sweep)
                    double fc = 1200.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 750.0;
                    st.F1L.SetLowPass(sr, fc, 0.75);
                    break;
                }
                case BlockOp.FuzzBigMuff:
                {
                    // EHX Big Muff Pi 실제 병렬 RC 크로스페이드 패시브 톤스택:
                    // LPF 브랜치 (22kΩ/10nF ~480Hz) + HPF 브랜치 (39kΩ/4nF ~1280Hz)
                    // Tone=5 중앙에서 자연스럽게 1kHz 미드 스쿱 발생, 양 끝(0/10)에서는 스쿱 소멸!
                    st.F0L.SetLowPass(sr, 480.0, 0.65);
                    st.F1L.SetHighPass(sr, 1280.0, 0.65);
                    st.F2L.SetLowPass(sr, 6400.0, 0.707);
                    break;
                }
                case BlockOp.OdSd1:
                    st.F0L.SetHighPass(sr, 580.0, 0.707);
                    st.F1L.SetLowPass(sr, 900.0 + 3400.0 * Math.Clamp(sl.P1 / 10.0, 0.0, 1.0), 0.707);
                    break;
                case BlockOp.BoostEp:
                    st.F0L.SetHighShelf(sr, 3000.0, sl.P1 > 0.5f ? 3.5 : 0.8, 0.75);
                    break;
                case BlockOp.CompBossCs3:
                    st.F0L.SetHighShelf(sr, 2600.0, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.6, 0.75);
                    st.F0R.SetHighShelf(sr, 2600.0, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.6, 0.75);
                    break;
                case BlockOp.CompOrangeSqueezer:
                    st.F0L.SetHighShelf(sr, 2800.0, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.5, 0.75);
                    st.F0R.SetHighShelf(sr, 2800.0, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.5, 0.75);
                    break;
                case BlockOp.OdBluesbreaker:
                    st.F1L.SetLowPass(sr, 1100.0 + 4800.0 * Math.Clamp(sl.P1 / 10.0, 0.0, 1.0), 0.707);
                    break;
                case BlockOp.DistGuvnor:
                    st.F1L.SetPeaking(sr, 110.0, 0.85, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 2.2);
                    st.F2L.SetPeaking(sr, 750.0, 0.95, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.2);
                    st.F3L.SetHighShelf(sr, 3200.0, (Math.Clamp(sl.P3, 0f, 10f) - 5.0) * 2.2, 0.75);
                    st.F4L.SetLowPass(sr, 7500.0, 0.707);
                    break;
                case BlockOp.FuzzFace:
                    st.F1L.SetLowPass(sr, 7500.0, 0.707);
                    break;
                case BlockOp.BoostRangemaster:
                    st.F1L.SetLowPass(sr, 6800.0, 0.707);
                    break;
                case BlockOp.DistPlus:
                    st.F1L.SetLowPass(sr, 5800.0, 0.707);
                    break;
                case BlockOp.OdBoxOfRock:
                    st.F1L.SetLowPass(sr, 950.0 + 4500.0 * Math.Clamp(sl.P1 / 10.0, 0.0, 1.0), 0.707);
                    break;
                case BlockOp.DistBossDs1:
                {
                    // BOSS DS-1 실제 병렬 RC 크로스페이드 패시브 톤스택:
                    // LPF 브랜치 (2.2kΩ/100nF ~240Hz 부하 포함) + HPF 브랜치 (6.8kΩ/22nF ~1064Hz)
                    // Tone 중앙 부근에서 자연스러운 ~650Hz 미드 딥 발생!
                    st.F0L.SetLowPass(sr, 240.0, 0.65);
                    st.F1L.SetHighPass(sr, 1064.0, 0.65);
                    st.F2L.SetLowPass(sr, 6200.0, 0.707);
                    break;
                }
                case BlockOp.OdBossBd2:
                {
                    // BOSS BD-2 Blues Driver: open full-range JFET preamp overdrive
                    double t = Math.Clamp(sl.P1 / 10.0, 0.0, 1.0);
                    st.F0L.SetHighPass(sr, 75.0, 0.707);
                    st.F1L.SetHighShelf(sr, 2400.0, (t - 0.45) * 10.5, 0.72);
                    st.F2L.SetLowPass(sr, 4800.0 + t * 4800.0, 0.707);
                    break;
                }
                case BlockOp.DistBossMt2:
                {
                    // BOSS MT-2 Metal Zone: pre-tightening HPF + 3-band EQ with sweepable Parametric Mid (200Hz..5kHz)
                    st.F0L.SetHighPass(sr, 190.0, 0.75);
                    st.F1L.SetPeaking(sr, 110.0, 0.9, Math.Clamp(sl.P2, -15f, 15f)); // LOW
                    st.F2L.SetPeaking(sr, Math.Clamp(sl.P4 > 100f ? sl.P4 : 1000f, 200f, 5000f), 1.35, Math.Clamp(sl.P3, -15f, 15f)); // MID + MID FREQ
                    st.F3L.SetHighShelf(sr, 3800.0, Math.Clamp(sl.P1, -15f, 15f), 0.72); // HIGH
                    st.F4L.SetLowPass(sr, 7200.0, 0.707);
                    break;
                }
                case BlockOp.DistBossHm2:
                {
                    // BOSS HM-2 Heavy Metal: Swedish chainsaw dual gyrator peaks (~95Hz Low + ~1.15kHz High)
                    double lowG = (Math.Clamp(sl.P1, 0f, 10f) - 4.0) * 2.4;
                    double hiG = (Math.Clamp(sl.P2, 0f, 10f) - 3.5) * 2.8;
                    st.F0L.SetHighPass(sr, 95.0, 0.707);
                    st.F1L.SetPeaking(sr, 95.0, 1.15, lowG);
                    st.F2L.SetPeaking(sr, 1180.0, 1.45, hiG);
                    st.F3L.SetHighShelf(sr, 3200.0, hiG * 0.45, 0.75);
                    st.F4L.SetLowPass(sr, 6800.0, 0.707);
                    break;
                }
                case BlockOp.CabIr:
                    // 독립 듀얼-캐비넷 Partitioned FFT 컨볼버 풀 배정 + 듀얼 가상 마이킹 + LowCut/HighCut
                    st.CabPoolIdx = (cabIdx++) % _cabPool.Length;
                    if (modelChanged || st.CabPoolIdx != prevCabIdx)
                        _cabPool[st.CabPoolIdx].Reset();
                    st.F0L.SetHighPass(sr, Math.Clamp(sl.P0 > 10f ? sl.P0 : 80f, 20f, 400f), 0.707);
                    st.F1L.SetLowPass(sr, Math.Clamp(sl.P1 > 500f ? sl.P1 : 9500f, 1500f, 18000f), 0.707);
                    st.F0R.SetHighPass(sr, Math.Clamp(sl.P0 > 10f ? sl.P0 : 80f, 20f, 400f), 0.707);
                    st.F1R.SetLowPass(sr, Math.Clamp(sl.P1 > 500f ? sl.P1 : 9500f, 1500f, 18000f), 0.707);
                    if (sl.CabMic.DualEnabled)
                    {
                        var cu = _cabPool[st.CabPoolIdx];
                        ConfigureMicVoicing(sr, sl.CabMic.MicAType, sl.CabMic.MicAX, sl.CabMic.MicADist,
                            ref cu.A0L, ref cu.A1L, ref cu.A2L, ref cu.A0R, ref cu.A1R, ref cu.A2R);
                        ConfigureMicVoicing(sr, sl.CabMic.MicBType, sl.CabMic.MicBX, sl.CabMic.MicBDist,
                            ref cu.B0L, ref cu.B1L, ref cu.B2L, ref cu.B0R, ref cu.B1R, ref cu.B2R);
                    }
                    break;
                case BlockOp.EqGraphic5:
                    st.F0L.SetPeaking(sr, 100.0, 1.1, Math.Clamp(sl.P0, -12f, 12f));
                    st.F1L.SetPeaking(sr, 300.0, 1.2, Math.Clamp(sl.P1, -12f, 12f));
                    st.F2L.SetPeaking(sr, 800.0, 1.2, Math.Clamp(sl.P2, -12f, 12f));
                    st.F3L.SetPeaking(sr, 2200.0, 1.2, Math.Clamp(sl.P3, -12f, 12f));
                    st.F4L.SetHighShelf(sr, 5500.0, Math.Clamp(sl.P4, -12f, 12f), 0.75);
                    st.F0R.SetPeaking(sr, 100.0, 1.1, Math.Clamp(sl.P0, -12f, 12f));
                    st.F1R.SetPeaking(sr, 300.0, 1.2, Math.Clamp(sl.P1, -12f, 12f));
                    st.F2R.SetPeaking(sr, 800.0, 1.2, Math.Clamp(sl.P2, -12f, 12f));
                    st.F3R.SetPeaking(sr, 2200.0, 1.2, Math.Clamp(sl.P3, -12f, 12f));
                    st.F4R.SetHighShelf(sr, 5500.0, Math.Clamp(sl.P4, -12f, 12f), 0.75);
                    break;
                case BlockOp.EqBossGe7:
                    // BOSS GE-7: 100, 200, 400, 800, 1.6k, 3.2k, 6.4kHz (+/-15dB)
                    st.F0L.SetPeaking(sr, 100.0, 1.4, Math.Clamp(sl.P0, -15f, 15f));
                    st.F1L.SetPeaking(sr, 200.0, 1.4, Math.Clamp(sl.P1, -15f, 15f));
                    st.F2L.SetPeaking(sr, 400.0, 1.4, Math.Clamp(sl.P2, -15f, 15f));
                    st.F3L.SetPeaking(sr, 800.0, 1.4, Math.Clamp(sl.P3, -15f, 15f));
                    st.F4L.SetPeaking(sr, 1600.0, 1.4, Math.Clamp(sl.P4, -15f, 15f));
                    st.F5L.SetPeaking(sr, 3200.0, 1.4, Math.Clamp(sl.P5, -15f, 15f));
                    st.F6L.SetPeaking(sr, 6400.0, 1.4, Math.Clamp(sl.P6, -15f, 15f));
                    st.F0R.SetPeaking(sr, 100.0, 1.4, Math.Clamp(sl.P0, -15f, 15f));
                    st.F1R.SetPeaking(sr, 200.0, 1.4, Math.Clamp(sl.P1, -15f, 15f));
                    st.F2R.SetPeaking(sr, 400.0, 1.4, Math.Clamp(sl.P2, -15f, 15f));
                    st.F3R.SetPeaking(sr, 800.0, 1.4, Math.Clamp(sl.P3, -15f, 15f));
                    st.F4R.SetPeaking(sr, 1600.0, 1.4, Math.Clamp(sl.P4, -15f, 15f));
                    st.F5R.SetPeaking(sr, 3200.0, 1.4, Math.Clamp(sl.P5, -15f, 15f));
                    st.F6R.SetPeaking(sr, 6400.0, 1.4, Math.Clamp(sl.P6, -15f, 15f));
                    break;
                case BlockOp.EqParametric:
                    st.F0L.SetHighPass(sr, Math.Clamp(sl.P0 > 15f ? sl.P0 : 60f, 20f, 400f), 0.707);
                    st.F1L.SetPeaking(sr, Math.Clamp(sl.P1 > 50f ? sl.P1 : 800f, 80f, 8000f), Math.Clamp(sl.P3 > 0.2f ? sl.P3 : 1.2f, 0.3f, 8f), Math.Clamp(sl.P2, -12f, 12f));
                    st.F2L.SetLowPass(sr, Math.Clamp(sl.P4 > 500f ? sl.P4 : 12000f, 1200f, 18000f), 0.707);
                    st.F0R.SetHighPass(sr, Math.Clamp(sl.P0 > 15f ? sl.P0 : 60f, 20f, 400f), 0.707);
                    st.F1R.SetPeaking(sr, Math.Clamp(sl.P1 > 50f ? sl.P1 : 800f, 80f, 8000f), Math.Clamp(sl.P3 > 0.2f ? sl.P3 : 1.2f, 0.3f, 8f), Math.Clamp(sl.P2, -12f, 12f));
                    st.F2R.SetLowPass(sr, Math.Clamp(sl.P4 > 500f ? sl.P4 : 12000f, 1200f, 18000f), 0.707);
                    break;
                case BlockOp.DlyAnalog:
                case BlockOp.DlyBossDm2w:
                case BlockOp.DlyDigital:
                case BlockOp.DlyBossDd3t:
                case BlockOp.DlyTimeLine:
                    if (dlyIdx < _delayPool.Length)
                    {
                        st.DelayPoolIdx = dlyIdx++;
                        if (modelChanged || st.DelayPoolIdx != prevDlyIdx)
                            _delayPool[st.DelayPoolIdx].Reset();
                    }
                    break;
                case BlockOp.RevSpring:
                case BlockOp.RevHall:
                case BlockOp.RevPlate:
                case BlockOp.RevBlueSky:
                case BlockOp.RevBossRv6:
                    if (revIdx < _reverbPool.Length)
                    {
                        st.ReverbPoolIdx = revIdx++;
                        if (modelChanged || st.ReverbPoolIdx != prevRevIdx)
                            _reverbPool[st.ReverbPoolIdx].Reset();
                    }
                    break;
            }
        }

        _appliedVersion = ver;
    }

    /// <summary>
    /// ToneProcessor RT 콜백에서 호출. 할당/잠금 0.
    /// </summary>
    internal void Process(
        Span<float> chL,
        Span<float> chR,
        int frames,
        ToneProcessor host)
    {
        if (frames <= 0) return;
        EnsureApplied();

        int n = Math.Min(frames, _maxFrames);
        var snap = _activeSnapshot;

        float paramAlpha = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.005f * _sampleRate));
        if (!_smoothReady)
        {
            _smoothInGain = snap.InputGain;
            _smoothOutGain = snap.OutputGain;
            _smoothParMix = snap.ParallelMix;
            _smoothReady = true;
        }

        // Input Trim (per-sample smoothed to eliminate zipper noise)
        if (MathF.Abs(_smoothInGain - 1f) > 1e-4f || MathF.Abs(snap.InputGain - 1f) > 1e-4f)
        {
            float target = snap.InputGain;
            for (int i = 0; i < n; i++)
            {
                _smoothInGain += paramAlpha * (target - _smoothInGain);
                chL[i] *= _smoothInGain;
                chR[i] *= _smoothInGain;
            }
        }

        if (snap.Routing == ChainRoutingMode.Serial)
        {
            for (int i = 0; i < SlotCount; i++)
                ProcessSlot(i, ref snap.Slots[i], chL, chR, n, host);
        }
        else
        {
            int splitCol = Math.Clamp(snap.SplitCol, 0, 7);
            int mergeCol = Math.Clamp(snap.MergeCol, splitCol, 7);

            // 1) Row 1 up to splitCol (inclusive)
            for (int col = 0; col <= splitCol; col++)
                ProcessSlot(col, ref snap.Slots[col], chL, chR, n, host);

            // 2) Copy split signal to parallel Row 2 buffers
            chL.Slice(0, n).CopyTo(_parL);
            chR.Slice(0, n).CopyTo(_parR);

            // 3) Row 1 from splitCol+1 .. mergeCol
            for (int col = splitCol + 1; col <= mergeCol; col++)
                ProcessSlot(col, ref snap.Slots[col], chL, chR, n, host);

            // 4) Row 2 (slots 8..15)
            bool row2HasActive = false;
            for (int col = 8; col < 16; col++)
            {
                if (snap.Slots[col].Op != BlockOp.None && snap.Slots[col].Enabled)
                {
                    row2HasActive = true;
                    ProcessSlot(col, ref snap.Slots[col], _parL.AsSpan(0, n), _parR.AsSpan(0, n), n, host);
                }
            }

            // 5) Merge Row 2 back into Row 1 (per-sample smoothed ParallelMix)
            if (row2HasActive)
            {
                float targetMix = snap.ParallelMix;
                for (int i = 0; i < n; i++)
                {
                    _smoothParMix += paramAlpha * (targetMix - _smoothParMix);
                    float mainG = 1f - _smoothParMix * 0.5f;
                    float parG = _smoothParMix;
                    chL[i] = chL[i] * mainG + _parL[i] * parG;
                    chR[i] = chR[i] * mainG + _parR[i] * parG;
                }
            }
            else
            {
                _smoothParMix = snap.ParallelMix;
            }

            // 6) Remaining Row 1 after mergeCol
            for (int col = mergeCol + 1; col < 8; col++)
                ProcessSlot(col, ref snap.Slots[col], chL, chR, n, host);
        }

        // Output Trim (per-sample smoothed)
        if (MathF.Abs(_smoothOutGain - 1f) > 1e-4f || MathF.Abs(snap.OutputGain - 1f) > 1e-4f)
        {
            float target = snap.OutputGain;
            for (int i = 0; i < n; i++)
            {
                _smoothOutGain += paramAlpha * (target - _smoothOutGain);
                chL[i] *= _smoothOutGain;
                chR[i] *= _smoothOutGain;
            }
        }
    }

    private void ProcessSlot(int idx, ref CompiledSlot sl, Span<float> bufL, Span<float> bufR, int n, ToneProcessor host)
    {
        if (sl.Op == BlockOp.None || !sl.Enabled) return;
        var st = _states[idx];
        int sr = _sampleRate;

        switch (sl.Op)
        {
            case BlockOp.GateAdaptive:
                ProcessGate(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2);
                break;

            case BlockOp.CompDyna:
                ProcessCompressor(st, bufL, bufR, n, sr, sens: sl.P0, outLvl: sl.P1, atkMs: Math.Clamp(sl.P2, 1f, 60f), mixPct: Math.Clamp(sl.P3, 0f, 100f), is1176: false);
                break;

            case BlockOp.Comp1176:
                ProcessCompressor(st, bufL, bufR, n, sr, sens: sl.P0, outLvl: sl.P1, atkMs: 0.8f, mixPct: Math.Clamp(sl.P3, 0f, 100f), is1176: true, ratio: Math.Clamp(sl.P2, 2f, 20f));
                break;

            case BlockOp.CompBossCs3:
                ProcessBossCs3(st, bufL, bufR, n, sr, level: sl.P0, attack: sl.P2, sustain: sl.P3);
                break;

            case BlockOp.CompSp:
                // Xotic SP Compressor: Blend(P0), Volume(P1), CompSwitch 0=Lo/1=Mid/2=Hi (P2)
                ProcessCompressor(
                    st, bufL, bufR, n, sr,
                    sens: sl.P2 < 0.5f ? 3.2f : sl.P2 < 1.5f ? 5.8f : 8.2f,
                    outLvl: sl.P1,
                    atkMs: sl.P2 < 0.5f ? 18f : sl.P2 < 1.5f ? 10f : 4f,
                    mixPct: Math.Clamp(sl.P0, 0f, 100f),
                    is1176: false);
                break;

            case BlockOp.CompOrangeSqueezer:
                ProcessOrangeSqueezer(st, bufL, bufR, n, sr, sl.P0, sl.P2, sl.P3);
                break;

            case BlockOp.OdTs808:
            case BlockOp.OdTs9:
            case BlockOp.OdKlon:
            case BlockOp.OdPureSky:
            case BlockOp.OdJanRay:
            case BlockOp.OdBbPreamp:
            case BlockOp.OdOcd:
            case BlockOp.OdBluesbreaker:
            case BlockOp.DistRat:
            case BlockOp.DistRat2:
            case BlockOp.DistEcstasy:
            case BlockOp.DistGuvnor:
            case BlockOp.DistPlus:
            case BlockOp.FuzzBigMuff:
            case BlockOp.FuzzFace:
            case BlockOp.OdSd1:
            case BlockOp.OdBoxOfRock:
            case BlockOp.BoostEp:
            case BlockOp.BoostRc:
            case BlockOp.BoostMicroAmp:
            case BlockOp.BoostRangemaster:
            case BlockOp.DistBossDs1:
            case BlockOp.OdBossBd2:
            case BlockOp.DistBossMt2:
            case BlockOp.DistBossHm2:
                ProcessNonlinearPedal4x(st, bufL, bufR, n, ref sl);
                break;

            case BlockOp.AmpWhiteBox:
                ProcessWhiteBoxAmp(st, bufL, bufR, n, host, ref sl);
                break;

            case BlockOp.AmpJc120:
                ProcessJc120(st, bufL, bufR, n, sl.P0, sl.P4);
                break;

            case BlockOp.NamCapture:
                ProcessNamBlock(bufL, bufR, n, host, ref sl);
                break;

            case BlockOp.CabIr:
                ProcessCabBlock(st, bufL, bufR, n, host, ref sl);
                break;

            case BlockOp.EqGraphic5:
                ProcessEq5(st, bufL, bufR, n, sl.P5);
                break;

            case BlockOp.EqBossGe7:
                ProcessEq7(st, bufL, bufR, n, sl.P7);
                break;

            case BlockOp.EqParametric:
                ProcessEqParam(st, bufL, bufR, n);
                break;

            case BlockOp.EqCryBaby:
                ProcessCryBabyWah(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2, sl.P3);
                break;

            case BlockOp.ModCe2:
                ProcessChorus(st, bufL, bufR, n, sr, ResolveSyncedRateHz(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 0.1f, 6.0f), sl.P1, sl.P2);
                break;

            case BlockOp.ModBossDc2w:
                ProcessBossDc2w(st, bufL, bufR, n, sr, sl.P0, sl.P1);
                break;

            case BlockOp.ModBossBf2:
                ProcessBossBf2(st, bufL, bufR, n, sr, sl.P0, sl.P1, ResolveSyncedRateHz(sl.P2, sl.SyncDiv, _activeSnapshot.TempoBpm, 0.08f, 6.0f), sl.P3);
                break;

            case BlockOp.ModSmallClone:
                ProcessSmallClone(st, bufL, bufR, n, sr, ResolveSyncedRateHz(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 0.1f, 5.5f), sl.P1, sl.P2);
                break;

            case BlockOp.ModUniVibe:
                ProcessUniVibe(st, bufL, bufR, n, sr, ResolveSyncedRateHz(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 0.2f, 8.0f), sl.P1, sl.P2);
                break;

            case BlockOp.ModPhase90:
                ProcessPhaser(st, bufL, bufR, n, sr, ResolveSyncedRateHz(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 0.1f, 6.0f), sl.P1, sl.P2, sl.P3);
                break;

            case BlockOp.ModTremolo:
                ProcessTremolo(st, bufL, bufR, n, sr, ResolveSyncedRateHz(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 0.5f, 12.0f), sl.P1, sl.P2);
                break;

            case BlockOp.DlyAnalog:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, ResolveSyncedDelayMs(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 40f, 1200f), sl.P1, sl.P2, sl.P3, isAnalog: true);
                break;

            case BlockOp.DlyBossDm2w:
                if (st.DelayPoolIdx >= 0)
                {
                    bool custom = sl.P3 > 0.5f;
                    float maxMs = custom ? 800f : 300f;
                    float minMs = custom ? 40f : 20f;
                    float timeMs = ResolveSyncedDelayMs(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, minMs, maxMs);
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, timeMs, sl.P1, custom ? 68f : 38f, sl.P2, isAnalog: true);
                }
                break;

            case BlockOp.DlyDigital:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, ResolveSyncedDelayMs(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 40f, 1350f), sl.P1, sl.P2, sl.P3, isAnalog: false);
                break;

            case BlockOp.DlyBossDd3t:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, ResolveSyncedDelayMs(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 20f, 800f), sl.P1, 0f, sl.P2, isAnalog: false);
                break;

            case BlockOp.DlyTimeLine:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, ResolveSyncedDelayMs(sl.P0, sl.SyncDiv, _activeSnapshot.TempoBpm, 30f, 1400f), sl.P1, sl.P2, sl.P3, isAnalog: sl.P4 > 0.5f);
                break;

            case BlockOp.RevSpring:
                if (st.ReverbPoolIdx >= 0)
                    _reverbPool[st.ReverbPoolIdx].Process(bufL, bufR, n, sr, sl.P0, sl.P1, 8f, sl.P2, isSpring: true, isPlate: false);
                break;

            case BlockOp.RevHall:
                if (st.ReverbPoolIdx >= 0)
                    _reverbPool[st.ReverbPoolIdx].Process(bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2, sl.P3, isSpring: false, isPlate: false);
                break;

            case BlockOp.RevPlate:
                if (st.ReverbPoolIdx >= 0)
                    _reverbPool[st.ReverbPoolIdx].Process(bufL, bufR, n, sr, sl.P0, sl.P1, 14f, sl.P2, isSpring: false, isPlate: true);
                break;

            case BlockOp.RevBlueSky:
                if (st.ReverbPoolIdx >= 0)
                {
                    // Strymon blueSky: Decay(P0), Pre-Delay(P1), High Damp(P2), Mix(P3), Shimmer(P4)
                    float shimAmt = Math.Clamp(sl.P4, 0f, 100f) / 100f;
                    _reverbPool[st.ReverbPoolIdx].Process(bufL, bufR, n, sr, sl.P0, sl.P2, sl.P1, sl.P3, isSpring: false, isPlate: true, shimmerAmt: shimAmt);
                }
                break;

            case BlockOp.RevBossRv6:
                if (st.ReverbPoolIdx >= 0)
                {
                    // BOSS RV-6: E.Level/Mix(P0), Tone/Damp(P1), Time/Decay(P2), Mode(P3: 0=Hall, 1=Plate, 2=Spring, 3=Shimmer)
                    int mode = Math.Clamp((int)MathF.Round(sl.P3), 0, 3);
                    float damp = 100f - Math.Clamp(sl.P1, 0f, 100f); // Higher Tone = less damping
                    _reverbPool[st.ReverbPoolIdx].Process(
                        bufL, bufR, n, sr,
                        decayPct: sl.P2,
                        dampPct: damp,
                        preDelayMs: mode == 2 ? 8f : 18f,
                        mixPct: sl.P0,
                        isSpring: mode == 2,
                        isPlate: mode == 1 || mode == 3,
                        shimmerAmt: mode == 3 ? 0.45f : 0f);
                }
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveSyncedDelayMs(float rawMs, byte syncDiv, float bpm, float minMs, float maxMs)
    {
        if (syncDiv == 0) return Math.Clamp(rawMs, minMs, maxMs);
        float beatMs = 60000f / Math.Clamp(bpm, 40f, 280f);
        float mult = syncDiv switch
        {
            1 => 1.0f,        // 1/4
            2 => 0.75f,       // 1/8. (Dotted Eighth)
            3 => 0.5f,        // 1/8
            4 => 0.33333334f, // 1/8T (Triplet)
            5 => 0.25f,       // 1/16
            _ => 1.0f,
        };
        return Math.Clamp(beatMs * mult, minMs, maxMs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ResolveSyncedRateHz(float rawHz, byte syncDiv, float bpm, float minHz, float maxHz)
    {
        if (syncDiv == 0) return Math.Clamp(rawHz, minHz, maxHz);
        float beatHz = Math.Clamp(bpm, 40f, 280f) / 60f;
        float mult = syncDiv switch
        {
            1 => 1.0f,        // 1/4
            2 => 1.3333334f,  // 1/8.
            3 => 2.0f,        // 1/8
            4 => 3.0f,        // 1/8T
            5 => 4.0f,        // 1/16
            _ => 1.0f,
        };
        return Math.Clamp(beatHz * mult, minHz, maxHz);
    }

    private static void ConfigureMicVoicing(
        int sr,
        byte micType,
        float xPos,
        float dist,
        ref Biquad f0L,
        ref Biquad f1L,
        ref Biquad f2L,
        ref Biquad f0R,
        ref Biquad f1R,
        ref Biquad f2R)
    {
        float x = Math.Clamp(xPos, 0f, 1f);
        float d = Math.Clamp(dist, 0f, 1f);

        // Proximity low-shelf (stronger on figure-8/hypercardioid ribbons R121 & M160)
        bool isRibbon = micType == 1 || micType == 4;
        double proxMax = isRibbon ? 4.8 : 3.2;
        double proxDb = (1.0 - d) * proxMax - d * 2.2;
        f0L.SetLowShelf(sr, 125.0, proxDb, 0.72);
        f0R.CopyCoeffsFrom(in f0L);

        // Capsule frequency contour
        (double midHz, double midQ, double midDb) = micType switch
        {
            0 => (3200.0, 1.10, 3.6),   // SM57: aggressive upper-mid bite
            1 => (4100.0, 0.90, -2.6),  // R121: warm smooth ribbon
            2 => (6200.0, 0.85, 2.8),   // C414: airy condenser top
            3 => (2300.0, 1.00, 2.4),   // MD421: tight mid punch
            4 => (3600.0, 0.95, -1.5),  // M160: focused ribbon
            5 => (1950.0, 0.85, 2.0),   // SM7B: smooth midrange presence
            _ => (3200.0, 1.00, 0.0),
        };
        f1L.SetPeaking(sr, midHz, midQ, midDb);
        f1R.CopyCoeffsFrom(in f1L);

        // Cap Center (11 kHz) -> Cone Edge (2.8 kHz) + distance air damping
        double offAxisHz = 11000.0 - x * 8200.0 - d * 1400.0;
        if (isRibbon) offAxisHz *= 0.84;
        f2L.SetLowPass(sr, Math.Clamp(offAxisHz, 1800.0, 14000.0), 0.707);
        f2R.CopyCoeffsFrom(in f2L);
    }
}