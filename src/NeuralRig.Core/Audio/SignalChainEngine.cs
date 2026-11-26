using System.Runtime.CompilerServices;
using System.Text.Json;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Serialization;
using NeuralRigStudio.Services;

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
        PitchCapo,
        PitchWhammy,
        PitchOctaver,
        CustomPedal,
        CustomAmp,
        CustomCab,
        CustomSpice,
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

    internal struct CustomAmpCircuitParams
    {
        public byte BaseAmpChannel; // 0=Clean, 1=Plexi, 2=HighGain
        public float PreampCathodeHz;
        public float VoicingMidHz;
        public float VoicingMidDb;
        public float HighShelfDb;
    }

    private struct CompiledSlot
    {
        public BlockOp Op;
        public byte SubModel; // Amp channel/voicing or Cab kind
        public byte SyncDiv;  // 0=Free, 1=1/4, 2=1/8., 3=1/8, 4=1/8T, 5=1/16
        public sbyte NamPoolIdx; // 0..2 for independent Multi-NAM, -1 if unassigned
        public int CustomHash;   // Identifies distinct custom schematic models
        public bool Enabled;
        public float P0, P1, P2, P3, P4, P5, P6, P7;
        public CabMicParams CabMic;
        public SchematicPedalDsp.CustomPedalCircuitParams CustomPedal;
        public CustomAmpCircuitParams CustomAmp;
    }

    private struct CompiledSceneSlot
    {
        public bool Enabled;
        public byte SyncDiv;
        public float P0, P1, P2, P3, P4, P5, P6, P7;
    }

    private sealed class CompiledSceneSnapshot
    {
        public float ParallelMix = 0.5f;
        public readonly CompiledSceneSlot[] Slots = new CompiledSceneSlot[SlotCount];
    }

    private sealed class CompiledSnapshot
    {
        public readonly CompiledSlot[] Slots = new CompiledSlot[SlotCount];
        public readonly NamWorkspace?[] NamWorkspaces = new NamWorkspace?[3];
        public readonly PartitionedIr?[] CustomCabIrs = new PartitionedIr?[SlotCount];
        public readonly RealtimeSpiceCircuit?[] CustomSpiceSlots = new RealtimeSpiceCircuit?[SlotCount];
        public CompiledSceneSnapshot[]? Scenes;
        public int ActiveScene;
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

        // 독립 리소스 풀 인덱스 (최대 4슬롯 동시 구동: 앰프 / 캐비넷 / 딜레이 / 리버브 / 피치)
        public BlockOp LastOp;
        public byte LastSubModel;
        public int LastCustomHash;
        public int AmpPoolIdx = -1;
        public int CabPoolIdx = -1;
        public int DelayPoolIdx = -1;
        public int ReverbPoolIdx = -1;
        public int PitchPoolIdx = -1;

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

    // ── 엔진 필드 (모든 풀 4슬롯 사전 할당, RT 할당 0) ──
    private readonly SlotDspState[] _states = new SlotDspState[SlotCount];
    private readonly WhiteBoxAmpEngine[] _ampPool = { new(), new(), new(), new() };
    private readonly CabinetIrBank _cabBank = new();
    private readonly CabSlotUnit[] _cabPool = { new(), new(), new(), new() };
    private readonly StereoDelayLine[] _delayPool = { new(), new(), new(), new() };
    private readonly StereoReverbTank[] _reverbPool = { new(), new(), new(), new() };
    private readonly PolyPitchEngine[] _pitchPool = { new(), new(), new(), new() };

    private CompiledSnapshot _pendingSnapshot = new();
    private CompiledSnapshot _activeSnapshot = new();
    private SignalChainConfigDto _lastDto = new();
    private Func<string?, ToneProfile?>? _namResolver;
    private readonly (string? modelId, ToneProfile? profile, NamWorkspace? ws)[] _uiNamPool = new (string?, ToneProfile?, NamWorkspace?)[3];
    private readonly (int hash, int sr, RealtimeSpiceCircuit? rt)[] _uiSpicePool = new (int, int, RealtimeSpiceCircuit?)[SlotCount];
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
    private bool _lastHostPlaying;
    private double _lastHostPpq;

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

    private CompiledSnapshot ReadLatestSnapshot()
    {
        int ver = Volatile.Read(ref _version);
        int applied = Volatile.Read(ref _appliedVersion);
        return applied == ver ? Volatile.Read(ref _activeSnapshot) : Volatile.Read(ref _pendingSnapshot);
    }

    public float TempoBpm => Volatile.Read(ref _pendingSnapshot).TempoBpm;
    public int ActiveScene => ReadLatestSnapshot().ActiveScene;
    public ChainRoutingMode RoutingMode => ReadLatestSnapshot().Routing;
    public float ParallelMix => ReadLatestSnapshot().ParallelMix;

    public int GetLatencySamples()
    {
        var snap = ReadLatestSnapshot();
        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            if (sl.Enabled && sl.Op is BlockOp.PitchCapo or BlockOp.PitchWhammy or BlockOp.PitchOctaver)
                return PolyPitchEngine.FftSize;
        }
        return 0;
    }

    public int GetTailSamples()
    {
        var snap = ReadLatestSnapshot();
        float maxTailSec = 0f;
        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            if (!sl.Enabled) continue;
            switch (sl.Op)
            {
                case BlockOp.RevSpring:
                case BlockOp.RevHall:
                case BlockOp.RevPlate:
                case BlockOp.RevBlueSky:
                    maxTailSec = MathF.Max(maxTailSec, sl.P0 > 7f || sl.P0 > 70f ? 8.0f : 6.0f);
                    break;
                case BlockOp.RevBossRv6:
                    maxTailSec = MathF.Max(maxTailSec, sl.P2 > 70f ? 8.0f : 6.0f);
                    break;
                case BlockOp.DlyAnalog:
                case BlockOp.DlyBossDm2w:
                case BlockOp.DlyDigital:
                case BlockOp.DlyBossDd3t:
                case BlockOp.DlyTimeLine:
                    maxTailSec = MathF.Max(maxTailSec, 4.0f);
                    break;
            }
        }
        return maxTailSec > 0f ? (int)Math.Round(maxTailSec * Math.Max(44100, _sampleRate)) : 0;
    }

    public int FindExpressionTargetSlot()
    {
        var snap = ReadLatestSnapshot();
        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            if (sl.Enabled && sl.Op is BlockOp.EqCryBaby or BlockOp.PitchWhammy)
                return i;
        }
        return -1;
    }

    public void SyncHostTransportRt(bool isPlaying, double ppqPosition, bool hasPpq, int frames = 0)
    {
        EnsureApplied();
        double expectedAdv = frames > 0
            ? ((double)frames / Math.Max(8000, _sampleRate)) * (_activeSnapshot.TempoBpm / 60.0)
            : 0.0;
        bool jumped = hasPpq && isPlaying &&
            ((ppqPosition - _lastHostPpq) < -0.02 ||
             Math.Abs((ppqPosition - _lastHostPpq) - expectedAdv) > 0.25);

        if ((isPlaying && !_lastHostPlaying) || jumped)
        {
            var snap = _activeSnapshot;
            for (int i = 0; i < SlotCount; i++)
            {
                ref var sl = ref snap.Slots[i];
                if (!sl.Enabled) continue;
                if (sl.Op is BlockOp.ModCe2 or BlockOp.ModBossDc2w or BlockOp.ModBossBf2
                    or BlockOp.ModSmallClone or BlockOp.ModUniVibe or BlockOp.ModPhase90 or BlockOp.ModTremolo)
                {
                    if (sl.SyncDiv > 0 && hasPpq)
                    {
                        double beatsPerCycle = sl.SyncDiv switch
                        {
                            1 => 1.0,
                            2 => 0.75,
                            3 => 0.5,
                            4 => 1.0 / 3.0,
                            5 => 0.25,
                            _ => 1.0,
                        };
                        double frac = (ppqPosition / beatsPerCycle) % 1.0;
                        if (frac < 0.0) frac += 1.0;
                        _states[i].LfoPhase = (float)(frac * (2.0 * Math.PI));
                    }
                    else if (!hasPpq || Math.Abs(ppqPosition) < 0.25)
                    {
                        _states[i].LfoPhase = 0f;
                    }
                }
            }
        }

        _lastHostPlaying = isPlaying;
        if (hasPpq) _lastHostPpq = ppqPosition;
    }

    /// <summary>DAW 오토메이션 및 실시간 MIDI용 무할당 RT 파라미터 갱신 API (힙 할당 0 B).</summary>
    public void SetGlobalParamsRt(
        float? inputGainDb = null,
        float? outputGainDb = null,
        float? parallelMix = null,
        ChainRoutingMode? routing = null)
    {
        EnsureApplied();
        var snap = _activeSnapshot;
        var pend = Volatile.Read(ref _pendingSnapshot);
        if (inputGainDb.HasValue)
        {
            float lin = MathF.Pow(10f, Math.Clamp(inputGainDb.Value, -24f, 24f) / 20f);
            snap.InputGain = lin;
            if (pend != null) pend.InputGain = lin;
        }
        if (outputGainDb.HasValue)
        {
            float lin = MathF.Pow(10f, Math.Clamp(outputGainDb.Value, -24f, 24f) / 20f);
            snap.OutputGain = lin;
            if (pend != null) pend.OutputGain = lin;
        }
        if (parallelMix.HasValue)
            snap.ParallelMix = Math.Clamp(parallelMix.Value, 0f, 1f);
        if (routing.HasValue)
            snap.Routing = routing.Value;
    }

    /// <summary>UI 슬라이더 드래그용 경량 I/O 게인 갱신 (슬롯/씬 재컴파일·DSP 리셋 없음).</summary>
    public void SetIoGainFromUi(float? inputGainDb = null, float? outputGainDb = null)
    {
        lock (_chainCompileLock)
        {
            var act = Volatile.Read(ref _activeSnapshot);
            var pend = Volatile.Read(ref _pendingSnapshot);
            if (inputGainDb.HasValue)
            {
                float db = MathF.Round(Math.Clamp(inputGainDb.Value, -24f, 24f), 2);
                float lin = MathF.Pow(10f, db / 20f);
                act.InputGain = lin;
                if (pend != null) pend.InputGain = lin;
                _lastDto.InputGainDb = db;
            }
            if (outputGainDb.HasValue)
            {
                float db = MathF.Round(Math.Clamp(outputGainDb.Value, -24f, 24f), 2);
                float lin = MathF.Pow(10f, db / 20f);
                act.OutputGain = lin;
                if (pend != null) pend.OutputGain = lin;
                _lastDto.OutputGainDb = db;
            }
        }
    }

    public void SetTempoBpmRt(float bpm)
    {
        float clamped = Math.Clamp(bpm > 1f ? bpm : 120f, 40f, 280f);
        EnsureApplied();
        _activeSnapshot.TempoBpm = clamped;
        var pend = Volatile.Read(ref _pendingSnapshot);
        if (pend != null) pend.TempoBpm = clamped;
    }

    public void SetSlotBypassRt(int slotIndex, bool enabled)
    {
        if ((uint)slotIndex >= SlotCount) return;
        EnsureApplied();
        _activeSnapshot.Slots[slotIndex].Enabled = enabled;
    }

    public bool GetSlotBypassRt(int slotIndex)
    {
        if ((uint)slotIndex >= SlotCount) return false;
        return ReadLatestSnapshot().Slots[slotIndex].Enabled;
    }

    public bool SwitchSceneRt(int sceneIndex)
    {
        EnsureApplied();
        int idx = Math.Clamp(sceneIndex, 0, 7);
        var snap = _activeSnapshot;
        var scenes = snap.Scenes;
        if (scenes is null || idx >= scenes.Length) return false;
        var sc = scenes[idx];
        snap.ActiveScene = idx;
        snap.ParallelMix = sc.ParallelMix;
        int sr = _sampleRate;
        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            ref readonly var scSlot = ref sc.Slots[i];
            sl.Enabled = scSlot.Enabled;
            sl.SyncDiv = scSlot.SyncDiv;
            sl.P0 = scSlot.P0; sl.P1 = scSlot.P1; sl.P2 = scSlot.P2; sl.P3 = scSlot.P3;
            sl.P4 = scSlot.P4; sl.P5 = scSlot.P5; sl.P6 = scSlot.P6; sl.P7 = scSlot.P7;
            ConfigureSlotFilters(_states[i], ref sl, sr);
        }
        return true;
    }

    public int GetPrimaryAmpSlotIndex()
    {
        var snap = ReadLatestSnapshot();
        for (int i = 0; i < SlotCount; i++)
        {
            if (snap.Slots[i].Op is BlockOp.AmpWhiteBox or BlockOp.AmpJc120 or BlockOp.CustomAmp)
                return i;
        }
        return -1;
    }

    public bool SetPrimaryAmpParamRt(int paramIdx, float value)
    {
        if ((uint)paramIdx > 7) return false;
        EnsureApplied();
        var snap = _activeSnapshot;
        int target = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            if (snap.Slots[i].Op is BlockOp.AmpWhiteBox or BlockOp.AmpJc120 or BlockOp.CustomAmp)
            {
                target = i;
                break;
            }
        }
        if (target < 0) return false;

        ref var sl = ref snap.Slots[target];
        switch (paramIdx)
        {
            case 0: sl.P0 = value; break;
            case 1: sl.P1 = value; break;
            case 2: sl.P2 = value; break;
            case 3: sl.P3 = value; break;
            case 4: sl.P4 = value; break;
            case 5: sl.P5 = value; break;
            case 6: sl.P6 = value; break;
            case 7: sl.P7 = value; break;
        }
        ConfigureSlotFilters(_states[target], ref sl, _sampleRate);
        return true;
    }

    public bool TryGetPrimaryAmpParam(int paramIdx, out float value)
    {
        value = 0f;
        if ((uint)paramIdx > 7) return false;
        var snap = ReadLatestSnapshot();
        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            if (sl.Op is BlockOp.AmpWhiteBox or BlockOp.AmpJc120 or BlockOp.CustomAmp)
            {
                value = paramIdx switch
                {
                    0 => sl.P0, 1 => sl.P1, 2 => sl.P2, 3 => sl.P3,
                    4 => sl.P4, 5 => sl.P5, 6 => sl.P6, _ => sl.P7,
                };
                return true;
            }
        }
        return false;
    }

    public bool SetSlotParamRt(int slotIndex, int paramIdx, float value)
    {
        if ((uint)slotIndex >= SlotCount || (uint)paramIdx > 7) return false;
        EnsureApplied();
        ref var sl = ref _activeSnapshot.Slots[slotIndex];
        switch (paramIdx)
        {
            case 0: sl.P0 = value; break;
            case 1: sl.P1 = value; break;
            case 2: sl.P2 = value; break;
            case 3: sl.P3 = value; break;
            case 4: sl.P4 = value; break;
            case 5: sl.P5 = value; break;
            case 6: sl.P6 = value; break;
            case 7: sl.P7 = value; break;
        }
        ConfigureSlotFilters(_states[slotIndex], ref sl, _sampleRate);
        return true;
    }

    public bool TryGetSlotParam(int slotIndex, int paramIdx, out float value)
    {
        value = 0f;
        if ((uint)slotIndex >= SlotCount || (uint)paramIdx > 7) return false;
        var snap = ReadLatestSnapshot();
        ref var sl = ref snap.Slots[slotIndex];
        value = paramIdx switch
        {
            0 => sl.P0, 1 => sl.P1, 2 => sl.P2, 3 => sl.P3,
            4 => sl.P4, 5 => sl.P5, 6 => sl.P6, _ => sl.P7,
        };
        return true;
    }

    /// <summary>DAW 상태 저장용(비-RT 스레드): 현재 RT 스냅샷의 오토메이션 반영 값을 DTO에 동기화하여 반환한다.</summary>
    public SignalChainConfigDto ExportLiveStateSnapshot()
    {
        var dto = GetConfigSnapshot();
        var act = ReadLatestSnapshot();
        dto.ActiveScene = Math.Clamp(act.ActiveScene, 0, 7);
        dto.RoutingMode = (int)act.Routing;
        dto.ParallelMix = Math.Clamp(act.ParallelMix, 0f, 1f);
        dto.TempoBpm = Math.Clamp(act.TempoBpm, 40f, 280f);
        dto.InputGainDb = MathF.Round(Math.Clamp(20f * MathF.Log10(Math.Max(1e-4f, act.InputGain)), -24f, 24f), 4);
        dto.OutputGainDb = MathF.Round(Math.Clamp(20f * MathF.Log10(Math.Max(1e-4f, act.OutputGain)), -24f, 24f), 4);

        for (int i = 0; i < SlotCount && i < dto.Slots.Count; i++)
        {
            ref var sl = ref act.Slots[i];
            var dst = dto.Slots[i];
            dst.Enabled = sl.Enabled;
            dst.SyncDivision = sl.SyncDiv;
            dst.Params = new[] { sl.P0, sl.P1, sl.P2, sl.P3, sl.P4, sl.P5, sl.P6, sl.P7 };
        }
        return dto;
    }

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
        for (int i = 0; i < _pitchPool.Length; i++)
            _pitchPool[i].Prepare(_sampleRate);

        var pendSnap = Volatile.Read(ref _pendingSnapshot);
        var lastDto = Volatile.Read(ref _lastDto);
        if (pendSnap != null && lastDto?.Slots != null)
        {
            bool needsSpiceRecompile = false;
            for (int i = 0; i < SlotCount && i < lastDto.Slots.Count; i++)
            {
                if (pendSnap.Slots[i].Op == BlockOp.CustomCab &&
                    CustomDeviceRegistry.TryGet(lastDto.Slots[i].ModelId, out var cSpec) && cSpec != null)
                {
                    pendSnap.CustomCabIrs[i] = CustomDeviceRegistry.GetOrCompileCabIr(cSpec, _sampleRate);
                }
                else if (pendSnap.Slots[i].Op == BlockOp.CustomSpice && _uiSpicePool[i].sr != _sampleRate)
                {
                    needsSpiceRecompile = true;
                }
            }
            if (needsSpiceRecompile)
                UpdateChain(lastDto);
        }

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
            _states[i].LastCustomHash = 0;
            _uiSpicePool[i].rt?.Reset();
        }
        for (int i = 0; i < _delayPool.Length; i++) _delayPool[i].Reset();
        for (int i = 0; i < _reverbPool.Length; i++) _reverbPool[i].Reset();
        for (int i = 0; i < _pitchPool.Length; i++) _pitchPool[i].Reset();
        var act = _activeSnapshot;
        for (int i = 0; i < act.NamWorkspaces.Length; i++) act.NamWorkspaces[i]?.Reset();
        for (int i = 0; i < act.CustomSpiceSlots.Length; i++) act.CustomSpiceSlots[i]?.Reset();
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
                ActiveScene = Math.Clamp(clean.ActiveScene, 0, 7),
                Routing = (ChainRoutingMode)Math.Clamp(clean.RoutingMode, 0, 1),
                SplitCol = Math.Clamp(clean.SplitCol, 0, 7),
                MergeCol = Math.Clamp(clean.MergeCol, 0, 7),
                ParallelMix = Math.Clamp(clean.ParallelMix, 0f, 1f),
                TempoBpm = Math.Clamp(clean.TempoBpm > 1f ? clean.TempoBpm : 120f, 40f, 280f),
                InputGain = MathF.Pow(10f, Math.Clamp(clean.InputGainDb, -24f, 24f) / 20f),
                OutputGain = MathF.Pow(10f, Math.Clamp(clean.OutputGainDb, -24f, 24f) / 20f),
            };

            int namIdx = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var s = clean.Slots[i];
                var (op, sub) = ParseModelId(s.ModelId);
                int customHash = 0;
                SchematicPedalDsp.CustomPedalCircuitParams customPedal = default;
                CustomAmpCircuitParams customAmp = default;

                if (op == BlockOp.None && !string.IsNullOrWhiteSpace(s.ModelId) &&
                    CustomDeviceRegistry.TryGet(s.ModelId, out var customSpec) && customSpec != null)
                {
                    customHash = StringComparer.OrdinalIgnoreCase.GetHashCode(customSpec.Id) ^ (int)customSpec.CreatedAtUtc.Ticks;
                    if (customSpec.SpiceCircuit != null && customSpec.Category != "cab")
                    {
                        string spiceJson = JsonSerializer.Serialize(customSpec.SpiceCircuit, NeuralRigJsonContext.Default.SpiceCircuitDto);
                        int spiceHash = customHash ^ StringComparer.Ordinal.GetHashCode(spiceJson);
                        customHash = spiceHash;
                        var cachedSpice = _uiSpicePool[i];
                        if (cachedSpice.hash == spiceHash && cachedSpice.sr == _sampleRate && cachedSpice.rt != null)
                        {
                            snap.CustomSpiceSlots[i] = cachedSpice.rt;
                            op = BlockOp.CustomSpice;
                        }
                        else
                        {
                            var compiledSpice = RealtimeSpiceCircuit.Compile(customSpec.SpiceCircuit, _sampleRate * 4);
                            if (compiledSpice.Ok && compiledSpice.Circuit != null)
                            {
                                _uiSpicePool[i] = (spiceHash, _sampleRate, compiledSpice.Circuit);
                                snap.CustomSpiceSlots[i] = compiledSpice.Circuit;
                                op = BlockOp.CustomSpice;
                            }
                        }
                    }
                    else
                    {
                        switch (customSpec.Category)
                        {
                            case "amp":
                                op = BlockOp.CustomAmp;
                                customAmp = new CustomAmpCircuitParams
                                {
                                    BaseAmpChannel = customSpec.BaseAmpChannel,
                                    PreampCathodeHz = customSpec.PreampCathodeHz,
                                    VoicingMidHz = customSpec.VoicingMidHz,
                                    VoicingMidDb = customSpec.VoicingMidDb,
                                    HighShelfDb = customSpec.HighShelfDb,
                                };
                                break;
                            case "cab":
                                op = BlockOp.CustomCab;
                                snap.CustomCabIrs[i] = CustomDeviceRegistry.GetOrCompileCabIr(customSpec, _sampleRate);
                                break;
                            default:
                                op = BlockOp.CustomPedal;
                                customPedal = new SchematicPedalDsp.CustomPedalCircuitParams
                                {
                                    ClippingKind = customSpec.ClippingKind,
                                    InputHpfHz = customSpec.InputHpfHz,
                                    ToneMinHz = customSpec.ToneMinHz,
                                    ToneMaxHz = customSpec.ToneMaxHz,
                                    MidPeakHz = customSpec.MidPeakHz,
                                    MidPeakDb = customSpec.MidPeakDb,
                                    MaxGainDb = customSpec.MaxGainDb,
                                    Asymmetry = customSpec.Asymmetry,
                                };
                                break;
                        }
                    }
                }

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
                    CustomHash = customHash,
                    Enabled = s.Enabled,
                    P0 = p[0], P1 = p[1], P2 = p[2], P3 = p[3],
                    P4 = p[4], P5 = p[5], P6 = p[6], P7 = p[7],
                    CabMic = micParams,
                    CustomPedal = customPedal,
                    CustomAmp = customAmp,
                };
            }

            // DAW RT 오토메이션에서 Scene A~H를 0 B 힙 할당으로 즉시 전환할 수 있도록 8씬 사전 컴파일
            var compiledScenes = new CompiledSceneSnapshot[8];
            for (int scIdx = 0; scIdx < 8; scIdx++)
            {
                var scDto = clean.Scenes != null && scIdx < clean.Scenes.Count ? clean.Scenes[scIdx] : null;
                var scCompiled = new CompiledSceneSnapshot
                {
                    ParallelMix = scDto != null ? Math.Clamp(scDto.ParallelMix, 0f, 1f) : snap.ParallelMix,
                };
                for (int i = 0; i < SlotCount; i++)
                {
                    if (scDto?.Slots != null && i < scDto.Slots.Count && scDto.Slots[i] != null)
                    {
                        var src = scDto.Slots[i];
                        var sp = src.Params ?? Array.Empty<float>();
                        float GetP(int k) => k < sp.Length ? sp[k] : 0f;
                        scCompiled.Slots[i] = new CompiledSceneSlot
                        {
                            Enabled = src.Enabled,
                            SyncDiv = (byte)Math.Clamp(src.SyncDivision, 0, 5),
                            P0 = GetP(0), P1 = GetP(1), P2 = GetP(2), P3 = GetP(3),
                            P4 = GetP(4), P5 = GetP(5), P6 = GetP(6), P7 = GetP(7),
                        };
                    }
                    else
                    {
                        ref var cur = ref snap.Slots[i];
                        scCompiled.Slots[i] = new CompiledSceneSlot
                        {
                            Enabled = cur.Enabled,
                            SyncDiv = cur.SyncDiv,
                            P0 = cur.P0, P1 = cur.P1, P2 = cur.P2, P3 = cur.P3,
                            P4 = cur.P4, P5 = cur.P5, P6 = cur.P6, P7 = cur.P7,
                        };
                    }
                }
                compiledScenes[scIdx] = scCompiled;
            }
            snap.Scenes = compiledScenes;

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
            "pitch_capo" => (BlockOp.PitchCapo, 0),
            "pitch_whammy" => (BlockOp.PitchWhammy, 0),
            "pitch_octaver" => (BlockOp.PitchOctaver, 0),
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
        int pitchIdx = 0;
        int sr = _sampleRate;

        for (int i = 0; i < SlotCount; i++)
        {
            ref var sl = ref snap.Slots[i];
            var st = _states[i];
            int prevAmpIdx = st.AmpPoolIdx;
            int prevCabIdx = st.CabPoolIdx;
            int prevDlyIdx = st.DelayPoolIdx;
            int prevRevIdx = st.ReverbPoolIdx;
            int prevPitchIdx = st.PitchPoolIdx;
            bool modelChanged = st.LastOp != sl.Op || st.LastSubModel != sl.SubModel || st.LastCustomHash != sl.CustomHash;
            if (modelChanged)
            {
                st.Reset();
                st.LastOp = sl.Op;
                st.LastSubModel = sl.SubModel;
                st.LastCustomHash = sl.CustomHash;
            }
            st.AmpPoolIdx = -1;
            st.CabPoolIdx = -1;
            st.DelayPoolIdx = -1;
            st.ReverbPoolIdx = -1;
            st.PitchPoolIdx = -1;

            switch (sl.Op)
            {
                case BlockOp.AmpWhiteBox:
                case BlockOp.CustomAmp:
                    // 독립 듀얼-앰프 풀 배정 (Row 1 + Row 2 병렬 듀얼 앰프 간 상태 충돌 완전 차단)
                    st.AmpPoolIdx = (ampIdx++) % _ampPool.Length;
                    if (modelChanged || st.AmpPoolIdx != prevAmpIdx)
                        _ampPool[st.AmpPoolIdx].Reset();
                    break;
                case BlockOp.CabIr:
                case BlockOp.CustomCab:
                    // 독립 듀얼-캐비넷 Partitioned FFT 컨볼버 풀 배정
                    st.CabPoolIdx = (cabIdx++) % _cabPool.Length;
                    if (modelChanged || st.CabPoolIdx != prevCabIdx)
                        _cabPool[st.CabPoolIdx].Reset();
                    break;
                case BlockOp.CustomSpice:
                    if (modelChanged)
                        snap.CustomSpiceSlots[i]?.Reset();
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
                case BlockOp.PitchCapo:
                case BlockOp.PitchWhammy:
                case BlockOp.PitchOctaver:
                    if (pitchIdx < _pitchPool.Length)
                    {
                        st.PitchPoolIdx = pitchIdx++;
                        if (modelChanged || st.PitchPoolIdx != prevPitchIdx)
                            _pitchPool[st.PitchPoolIdx].Reset();
                    }
                    break;
            }

            ConfigureSlotFilters(st, ref sl, sr);
        }

        _appliedVersion = ver;
    }

    private void ConfigureSlotFilters(SlotDspState st, ref CompiledSlot sl, int sr)
    {
        switch (sl.Op)
        {
                case BlockOp.OdTs808:
                {
                    double tNorm = Math.Clamp(sl.P1 / 10.0, 0.0, 1.0);
                    st.F0L.SetHighPass(sr, 720.0, 0.707);
                    st.F1L.SetLowPass(sr, 720.0 + 2800.0 * tNorm, 0.707);
                    st.F2L.SetHighShelf(sr, 2400.0, (tNorm - 0.45) * 9.5, 0.75);
                    break;
                }
                case BlockOp.OdTs9:
                {
                    double tNorm = Math.Clamp(sl.P1 / 10.0, 0.0, 1.0);
                    st.F0L.SetHighPass(sr, 760.0, 0.72);
                    st.F1L.SetLowPass(sr, 900.0 + 3200.0 * tNorm, 0.707);
                    st.F2L.SetHighShelf(sr, 2500.0, (tNorm - 0.40) * 10.5, 0.75);
                    break;
                }
                case BlockOp.OdPureSky:
                    st.F0L.SetHighPass(sr, 45.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 18.0, 0.707);
                    st.F1L.SetHighShelf(sr, 2600.0, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 1.9, 0.72);
                    st.F2L.SetLowPass(sr, 6500.0 + Math.Clamp(sl.P2, 0f, 10f) * 450.0, 0.707);
                    break;
                case BlockOp.OdJanRay:
                    st.F0L.SetHighPass(sr, 60.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 14.0, 0.707);
                    st.F1L.SetPeaking(sr, 130.0, 0.85, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.4);
                    st.F2L.SetHighShelf(sr, 3000.0, (Math.Clamp(sl.P2, 0f, 10f) - 4.5) * 1.8, 0.75);
                    st.F3L.SetLowPass(sr, 8500.0, 0.707);
                    break;
                case BlockOp.OdBbPreamp:
                    st.F0L.SetHighPass(sr, 90.0, 0.707);
                    st.F1L.SetPeaking(sr, 115.0, 0.85, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 2.2);
                    st.F2L.SetHighShelf(sr, 2800.0, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.2, 0.72);
                    st.F3L.SetLowPass(sr, 6200.0, 0.707);
                    break;
                case BlockOp.OdOcd:
                {
                    bool hp = sl.P3 > 0.5f;
                    st.F0L.SetHighPass(sr, hp ? 130.0 : 80.0, 0.707);
                    st.F1L.SetPeaking(sr, 1400.0, 0.85, hp ? 3.2 : 0.4);
                    st.F2L.SetLowPass(sr, 1400.0 + Math.Clamp(sl.P1, 0f, 10f) * 520.0, 0.72);
                    break;
                }
                case BlockOp.DistRat2:
                {
                    st.F0L.SetHighPass(sr, 165.0, 0.75);
                    double fc = 1050.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 820.0;
                    st.F1L.SetLowPass(sr, fc, 0.72);
                    st.F2L.SetPeaking(sr, 1300.0, 1.0, 2.0);
                    break;
                }
                case BlockOp.DistEcstasy:
                    st.F0L.SetHighPass(sr, 145.0, 0.707);
                    st.F1L.SetPeaking(sr, 110.0, 0.9, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 2.0);
                    st.F2L.SetPeaking(sr, 750.0, 1.0, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.2);
                    st.F3L.SetHighShelf(sr, 3200.0, (Math.Clamp(sl.P3, 0f, 10f) - 5.0) * 2.0, 0.75);
                    st.F4L.SetLowPass(sr, 7400.0, 0.707);
                    break;
                case BlockOp.BoostMicroAmp:
                    st.F0L.SetHighShelf(sr, 3800.0, 1.4, 0.75);
                    break;
                case BlockOp.BoostRc:
                    st.F0L.SetPeaking(sr, 110.0, 0.85, (Math.Clamp(sl.P2, 0f, 10f) - 5.0) * 2.4);
                    st.F1L.SetHighShelf(sr, 3200.0, (Math.Clamp(sl.P3, 0f, 10f) - 5.0) * 2.4, 0.75);
                    break;
                case BlockOp.AmpJc120:
                {
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
                    switch (sl.SubModel)
                    {
                        case 6:
                            st.F0L.SetPeaking(sr, 1950.0, 0.9, 3.2);
                            st.F1L.SetHighShelf(sr, 3600.0, 2.4, 0.75);
                            break;
                        case 7:
                            st.F0L.SetPeaking(sr, 950.0, 0.95, 2.0);
                            st.F1L.SetHighShelf(sr, 3200.0, 1.5, 0.75);
                            break;
                        case 8:
                            st.F0L.SetHighPass(sr, 95.0, 0.707);
                            st.F1L.SetPeaking(sr, 520.0, 0.9, -2.8);
                            break;
                        case 9:
                            st.F0L.SetPeaking(sr, 820.0, 0.95, 2.8);
                            st.F1L.SetHighShelf(sr, 4200.0, 1.2, 0.75);
                            break;
                        default:
                            st.F0L.SetBypass();
                            st.F1L.SetBypass();
                            break;
                    }
                    break;
                case BlockOp.OdKlon:
                    st.F1L.SetPeaking(sr, 1000.0, 0.9, 1.5 + 3.0 * Math.Clamp(sl.P0 / 10.0, 0.0, 1.0));
                    st.F2L.SetHighShelf(sr, 2200.0, (Math.Clamp(sl.P1, 0f, 10f) - 5.0) * 1.8, 0.75);
                    break;
                case BlockOp.DistRat:
                {
                    st.F0L.SetHighPass(sr, 140.0, 0.707);
                    double fc = 1200.0 + (10.0 - Math.Clamp(sl.P1, 0f, 10f)) * 750.0;
                    st.F1L.SetLowPass(sr, fc, 0.75);
                    break;
                }
                case BlockOp.FuzzBigMuff:
                    st.F0L.SetLowPass(sr, 480.0, 0.65);
                    st.F1L.SetHighPass(sr, 1280.0, 0.65);
                    st.F2L.SetLowPass(sr, 6400.0, 0.707);
                    break;
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
                    st.F0L.SetLowPass(sr, 240.0, 0.65);
                    st.F1L.SetHighPass(sr, 1064.0, 0.65);
                    st.F2L.SetLowPass(sr, 6200.0, 0.707);
                    break;
                case BlockOp.OdBossBd2:
                {
                    double t = Math.Clamp(sl.P1 / 10.0, 0.0, 1.0);
                    st.F0L.SetHighPass(sr, 75.0, 0.707);
                    st.F1L.SetHighShelf(sr, 2400.0, (t - 0.45) * 10.5, 0.72);
                    st.F2L.SetLowPass(sr, 4800.0 + t * 4800.0, 0.707);
                    break;
                }
                case BlockOp.DistBossMt2:
                    st.F0L.SetHighPass(sr, 190.0, 0.75);
                    st.F1L.SetPeaking(sr, 110.0, 0.9, Math.Clamp(sl.P2, -15f, 15f));
                    st.F2L.SetPeaking(sr, Math.Clamp(sl.P4 > 100f ? sl.P4 : 1000f, 200f, 5000f), 1.35, Math.Clamp(sl.P3, -15f, 15f));
                    st.F3L.SetHighShelf(sr, 3800.0, Math.Clamp(sl.P1, -15f, 15f), 0.72);
                    st.F4L.SetLowPass(sr, 7200.0, 0.707);
                    break;
                case BlockOp.DistBossHm2:
                {
                    double lowG = (Math.Clamp(sl.P1, 0f, 10f) - 4.0) * 2.4;
                    double hiG = (Math.Clamp(sl.P2, 0f, 10f) - 3.5) * 2.8;
                    st.F0L.SetHighPass(sr, 95.0, 0.707);
                    st.F1L.SetPeaking(sr, 95.0, 1.15, lowG);
                    st.F2L.SetPeaking(sr, 1180.0, 1.45, hiG);
                    st.F3L.SetHighShelf(sr, 3200.0, hiG * 0.45, 0.75);
                    st.F4L.SetLowPass(sr, 6800.0, 0.707);
                    break;
                }
                case BlockOp.CustomPedal:
                {
                    double tNorm = Math.Clamp(sl.P1 / 10.0, 0.0, 1.0);
                    double cScale = 0.65 + 0.07 * Math.Clamp(sl.P3, 0f, 10f);
                    double lpHz = sl.CustomPedal.ToneMinHz + (sl.CustomPedal.ToneMaxHz - sl.CustomPedal.ToneMinHz) * tNorm;
                    st.F0L.SetLowPass(sr, Math.Clamp(lpHz, 450.0, 14000.0), 0.707);
                    st.F1L.SetPeaking(sr, Math.Clamp(sl.CustomPedal.MidPeakHz, 200f, 3500f), 0.95, Math.Clamp(sl.CustomPedal.MidPeakDb * cScale, -12.0, 12.0));
                    st.F2L.SetHighShelf(sr, 2800.0, (tNorm - 0.45) * 6.5, 0.75);
                    break;
                }
                case BlockOp.CustomAmp:
                {
                    st.F0L.SetHighPass(sr, Math.Clamp(sl.CustomAmp.PreampCathodeHz * 0.45, 35.0, 260.0), 0.707);
                    st.F1L.SetPeaking(sr, Math.Clamp(sl.CustomAmp.VoicingMidHz, 280f, 2800f), 0.92, sl.CustomAmp.VoicingMidDb);
                    st.F2L.SetHighShelf(sr, 3400.0, sl.CustomAmp.HighShelfDb, 0.75);
                    break;
                }
                case BlockOp.CabIr:
                case BlockOp.CustomCab:
                    st.F0L.SetHighPass(sr, Math.Clamp(sl.P0 > 10f ? sl.P0 : 80f, 20f, 400f), 0.707);
                    st.F1L.SetLowPass(sr, Math.Clamp(sl.P1 > 500f ? sl.P1 : 9500f, 1500f, 18000f), 0.707);
                    st.F0R.SetHighPass(sr, Math.Clamp(sl.P0 > 10f ? sl.P0 : 80f, 20f, 400f), 0.707);
                    st.F1R.SetLowPass(sr, Math.Clamp(sl.P1 > 500f ? sl.P1 : 9500f, 1500f, 18000f), 0.707);
                    if (sl.CabMic.DualEnabled && st.CabPoolIdx >= 0 && st.CabPoolIdx < _cabPool.Length)
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
        }
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

        float inPkL = 0f, inPkR = 0f;
        for (int i = 0; i < n; i++)
        {
            float al = MathF.Abs(chL[i]);
            float ar = MathF.Abs(chR[i]);
            if (al > inPkL) inPkL = al;
            if (ar > inPkR) inPkR = ar;
        }
        host.RecordInputPeakRt(inPkL, inPkR);

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
            case BlockOp.CustomPedal:
                ProcessNonlinearPedal4x(st, bufL, bufR, n, ref sl);
                break;

            case BlockOp.CustomSpice:
                ProcessCustomSpice(idx, st, bufL, bufR, n, ref sl);
                break;

            case BlockOp.AmpWhiteBox:
                ProcessWhiteBoxAmp(st, bufL, bufR, n, host, ref sl);
                break;

            case BlockOp.CustomAmp:
                ProcessCustomAmp(st, bufL, bufR, n, host, ref sl);
                break;

            case BlockOp.AmpJc120:
                ProcessJc120(st, bufL, bufR, n, sl.P0, sl.P4);
                break;

            case BlockOp.NamCapture:
                ProcessNamBlock(bufL, bufR, n, host, ref sl);
                break;

            case BlockOp.CabIr:
                ProcessCabBlock(st, bufL, bufR, n, host, ref sl, overridePartIr: null);
                break;

            case BlockOp.CustomCab:
                ProcessCabBlock(st, bufL, bufR, n, host, ref sl, overridePartIr: _activeSnapshot.CustomCabIrs[idx]);
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

            case BlockOp.PitchCapo:
                if (st.PitchPoolIdx >= 0)
                    _pitchPool[st.PitchPoolIdx].ProcessPitchShift(bufL, bufR, n, semitones: sl.P0, detuneCents: sl.P1, mixPct: sl.P2, levelDb: sl.P3);
                break;

            case BlockOp.PitchWhammy:
                if (st.PitchPoolIdx >= 0)
                {
                    float sweptSemis = (Math.Clamp(sl.P0, 0f, 100f) / 100f) * Math.Clamp(sl.P1, -24f, 24f);
                    _pitchPool[st.PitchPoolIdx].ProcessPitchShift(bufL, bufR, n, semitones: sweptSemis, detuneCents: 0f, mixPct: sl.P2, levelDb: sl.P3);
                }
                break;

            case BlockOp.PitchOctaver:
                if (st.PitchPoolIdx >= 0)
                    _pitchPool[st.PitchPoolIdx].ProcessOctaver(bufL, bufR, n, dryPct: sl.P0, subOctPct: sl.P1, upOctPct: sl.P2, toneKnob: sl.P3, levelDb: sl.P4);
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