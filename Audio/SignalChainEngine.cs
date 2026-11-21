using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

public enum ChainBlockCategory
{
    Empty = 0,
    Dynamics = 1,
    Drive = 2,
    Amp = 3,
    Nam = 4,
    Cab = 5,
    Eq = 6,
    Modulation = 7,
    Delay = 8,
    Reverb = 9,
}

public enum ChainRoutingMode
{
    Serial = 0,        // Row 1 (0..7) -> Row 2 (8..15) -> Out
    ParallelSplit = 1, // Row 1 splits at SplitCol into Row 2, merges at MergeCol
}

/// <summary>
/// 직렬화 가능한 시그널 체인 단일 슬롯 DTO (UI <-> C# 브리지 <-> settings.json).
/// </summary>
public sealed class ChainSlotDto
{
    public int Index { get; set; }
    public string ModelId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public float[] Params { get; set; } = new float[8];

    public ChainSlotDto Clone()
    {
        var p = new float[8];
        if (Params != null)
            Array.Copy(Params, p, Math.Min(8, Params.Length));
        return new ChainSlotDto
        {
            Index = Index,
            ModelId = ModelId ?? "",
            Enabled = Enabled,
            Params = p,
        };
    }
}

/// <summary>
/// 개별 씬(A..H)의 슬롯 ON/OFF 및 파라미터 스냅샷 DTO.
/// </summary>
public sealed class SceneSlotStateDto
{
    public bool Enabled { get; set; } = true;
    public float[] Params { get; set; } = new float[8];

    public SceneSlotStateDto Clone()
    {
        var p = new float[8];
        if (Params != null) Array.Copy(Params, p, Math.Min(8, Params.Length));
        return new SceneSlotStateDto { Enabled = Enabled, Params = p };
    }
}

public sealed class SceneSnapshotDto
{
    public float ParallelMix { get; set; } = 0.5f;
    public List<SceneSlotStateDto> Slots { get; set; } = new();

    public SceneSnapshotDto Clone()
    {
        var copy = new SceneSnapshotDto
        {
            ParallelMix = Math.Clamp(ParallelMix, 0f, 1f),
            Slots = new List<SceneSlotStateDto>(Slots?.Count ?? 0),
        };
        if (Slots != null)
        {
            for (int i = 0; i < Slots.Count; i++)
                copy.Slots.Add(Slots[i]?.Clone() ?? new SceneSlotStateDto());
        }
        return copy;
    }
}

/// <summary>
/// 직렬화 가능한 전체 시그널 체인 구성 DTO (2행 × 8슬롯 = 16슬롯 + 8개 씬 스냅샷).
/// </summary>
public sealed class SignalChainConfigDto
{
    public const int SlotCount = 16;

    public string PresetId { get; set; } = "1A";
    public string PresetName { get; set; } = "Beast Lead";
    public int ActiveScene { get; set; } = 3; // 0..7 (A..H)
    public int RoutingMode { get; set; } = (int)ChainRoutingMode.ParallelSplit;
    public int SplitCol { get; set; } = 1;    // 0..7 (Row 1에서 Row 2로 분기하는 컬럼)
    public int MergeCol { get; set; } = 5;    // 0..7 (Row 2가 Row 1로 합류하는 컬럼)
    public float ParallelMix { get; set; } = 0.42f; // 0..1 (Row 2 합류 비율)
    public float InputGainDb { get; set; } = 0f;
    public float OutputGainDb { get; set; } = 0f;
    public List<ChainSlotDto> Slots { get; set; } = CreateDefaultSlots();
    public List<SceneSnapshotDto>? Scenes { get; set; }

    public SignalChainConfigDto Clone()
    {
        var copy = new SignalChainConfigDto
        {
            PresetId = PresetId ?? "1A",
            PresetName = PresetName ?? "Custom",
            ActiveScene = Math.Clamp(ActiveScene, 0, 7),
            RoutingMode = Math.Clamp(RoutingMode, 0, 1),
            SplitCol = Math.Clamp(SplitCol, 0, 7),
            MergeCol = Math.Clamp(MergeCol, 0, 7),
            ParallelMix = Math.Clamp(ParallelMix, 0f, 1f),
            InputGainDb = Math.Clamp(InputGainDb, -18f, 18f),
            OutputGainDb = Math.Clamp(OutputGainDb, -18f, 18f),
            Slots = new List<ChainSlotDto>(SlotCount),
        };

        for (int i = 0; i < SlotCount; i++)
        {
            var src = Slots != null && i < Slots.Count ? Slots[i] : null;
            var slot = src != null ? src.Clone() : new ChainSlotDto { Index = i, ModelId = "", Enabled = true };
            slot.Index = i;
            copy.Slots.Add(slot);
        }

        if (Scenes != null && Scenes.Count > 0)
        {
            copy.Scenes = new List<SceneSnapshotDto>(Scenes.Count);
            for (int i = 0; i < Math.Min(8, Scenes.Count); i++)
                copy.Scenes.Add(Scenes[i]?.Clone() ?? new SceneSnapshotDto());
        }
        return copy;
    }

    public static List<ChainSlotDto> CreateDefaultSlots()
    {
        var list = new List<ChainSlotDto>(SlotCount);
        for (int i = 0; i < SlotCount; i++)
            list.Add(new ChainSlotDto { Index = i, ModelId = "", Enabled = true, Params = new float[8] });

        // 기본 1A Beast Lead: Gate -> Green 808 -> Brit Plexi 100 Bright -> 4x12 V30 -> Digital Delay -> Hall Reverb
        // Row 2 Parallel: Graphic EQ -> CE-2 Chorus
        list[0] = new ChainSlotDto { Index = 0, ModelId = "gate_adaptive", Enabled = true, Params = new float[] { -56f, 120f, 80f, 0, 0, 0, 0, 0 } };
        list[1] = new ChainSlotDto { Index = 1, ModelId = "od_ts808", Enabled = true, Params = new float[] { 3.5f, 5.8f, 6.2f, 0, 0, 0, 0, 0 } };
        list[2] = new ChainSlotDto { Index = 2, ModelId = "amp_plexi", Enabled = true, Params = new float[] { 6.2f, 5.2f, 5.8f, 6.0f, 5.5f, 3.5f, 5.5f, 1.0f } };
        list[3] = new ChainSlotDto { Index = 3, ModelId = "cab_v30", Enabled = true, Params = new float[] { 80f, 9500f, 0f, 0, 0, 0, 0, 0 } };
        list[5] = new ChainSlotDto { Index = 5, ModelId = "dly_digital", Enabled = true, Params = new float[] { 380f, 32f, 18f, 24f, 0, 0, 0, 0 } };
        list[6] = new ChainSlotDto { Index = 6, ModelId = "rev_hall", Enabled = true, Params = new float[] { 55f, 45f, 25f, 26f, 0, 0, 0, 0 } };

        list[8] = new ChainSlotDto { Index = 8, ModelId = "eq_graphic5", Enabled = false, Params = new float[] { 1.5f, -1.0f, 2.0f, 1.5f, 0.5f, 0f, 0, 0 } };
        list[9] = new ChainSlotDto { Index = 9, ModelId = "mod_ce2", Enabled = false, Params = new float[] { 1.1f, 48f, 45f, 0, 0, 0, 0, 0 } };
        return list;
    }
}

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
public sealed class SignalChainEngine
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

    private struct CompiledSlot
    {
        public BlockOp Op;
        public byte SubModel; // Amp channel/voicing or Cab kind
        public bool Enabled;
        public float P0, P1, P2, P3, P4, P5, P6, P7;
    }

    private sealed class CompiledSnapshot
    {
        public readonly CompiledSlot[] Slots = new CompiledSlot[SlotCount];
        public ChainRoutingMode Routing;
        public int SplitCol;
        public int MergeCol;
        public float ParallelMix;
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

    // ── 엔진 필드 (모든 풀 사전 할당, RT 할당 0) ──
    private readonly SlotDspState[] _states = new SlotDspState[SlotCount];
    private readonly WhiteBoxAmpEngine[] _ampPool = { new(), new() };
    private readonly CabinetIrBank _cabBank = new();
    private readonly CabinetIrBank.StereoCabConvolver[] _cabPool = { new(), new() };
    private readonly StereoDelayLine[] _delayPool = { new(), new() };
    private readonly StereoReverbTank[] _reverbPool = { new(), new() };

    private CompiledSnapshot _pendingSnapshot = new();
    private CompiledSnapshot _activeSnapshot = new();
    private SignalChainConfigDto _lastDto = new();
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
        _smoothReady = false;
        _appliedVersion = 0;
    }

    /// <summary>UI 스레드에서 호출: 체인 구성을 컴파일하여 RT 스레드에 원자적으로 전달한다.</summary>
    public void UpdateChain(SignalChainConfigDto dto)
    {
        var clean = (dto ?? new SignalChainConfigDto()).Clone();
        var snap = new CompiledSnapshot
        {
            Routing = (ChainRoutingMode)Math.Clamp(clean.RoutingMode, 0, 1),
            SplitCol = Math.Clamp(clean.SplitCol, 0, 7),
            MergeCol = Math.Clamp(clean.MergeCol, 0, 7),
            ParallelMix = Math.Clamp(clean.ParallelMix, 0f, 1f),
            InputGain = MathF.Pow(10f, Math.Clamp(clean.InputGainDb, -18f, 18f) / 20f),
            OutputGain = MathF.Pow(10f, Math.Clamp(clean.OutputGainDb, -18f, 18f) / 20f),
        };

        for (int i = 0; i < SlotCount; i++)
        {
            var s = clean.Slots[i];
            var (op, sub) = ParseModelId(s.ModelId);
            var p = s.Params;
            snap.Slots[i] = new CompiledSlot
            {
                Op = op,
                SubModel = sub,
                Enabled = s.Enabled,
                P0 = p[0], P1 = p[1], P2 = p[2], P3 = p[3],
                P4 = p[4], P5 = p[5], P6 = p[6], P7 = p[7],
            };
        }

        Volatile.Write(ref _lastDto, clean);
        Volatile.Write(ref _pendingSnapshot, snap);
        Interlocked.Increment(ref _version);
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
                    // 독립 듀얼-캐비넷 스테레오 컨볼버 풀 배정 + L/R 사용자 LowCut/HighCut
                    // (각 캐비넷 모델의 음향 특성은 CabinetIrBank의 7종 고유 512-tap Minimum-Phase IR에 직접 내장)
                    st.CabPoolIdx = (cabIdx++) % _cabPool.Length;
                    if (modelChanged || st.CabPoolIdx != prevCabIdx)
                        _cabPool[st.CabPoolIdx].Reset();
                    st.F0L.SetHighPass(sr, Math.Clamp(sl.P0 > 10f ? sl.P0 : 80f, 20f, 400f), 0.707);
                    st.F1L.SetLowPass(sr, Math.Clamp(sl.P1 > 500f ? sl.P1 : 9500f, 1500f, 18000f), 0.707);
                    st.F0R.SetHighPass(sr, Math.Clamp(sl.P0 > 10f ? sl.P0 : 80f, 20f, 400f), 0.707);
                    st.F1R.SetLowPass(sr, Math.Clamp(sl.P1 > 500f ? sl.P1 : 9500f, 1500f, 18000f), 0.707);
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
                ProcessNamBlock(bufL, bufR, n, host, sl.P0, sl.P1, sl.P2);
                break;

            case BlockOp.CabIr:
                ProcessCabBlock(st, bufL, bufR, n, host, sl.SubModel, sl.P2);
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
                ProcessChorus(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2);
                break;

            case BlockOp.ModBossDc2w:
                ProcessBossDc2w(st, bufL, bufR, n, sr, sl.P0, sl.P1);
                break;

            case BlockOp.ModBossBf2:
                ProcessBossBf2(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2, sl.P3);
                break;

            case BlockOp.ModSmallClone:
                ProcessSmallClone(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2);
                break;

            case BlockOp.ModUniVibe:
                ProcessUniVibe(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2);
                break;

            case BlockOp.ModPhase90:
                ProcessPhaser(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2, sl.P3);
                break;

            case BlockOp.ModTremolo:
                ProcessTremolo(st, bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2);
                break;

            case BlockOp.DlyAnalog:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2, sl.P3, isAnalog: true);
                break;

            case BlockOp.DlyBossDm2w:
                if (st.DelayPoolIdx >= 0)
                {
                    bool custom = sl.P3 > 0.5f;
                    float timeMs = custom ? Math.Clamp(sl.P0, 40f, 800f) : Math.Clamp(sl.P0, 20f, 300f);
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, timeMs, sl.P1, custom ? 68f : 38f, sl.P2, isAnalog: true);
                }
                break;

            case BlockOp.DlyDigital:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, sl.P0, sl.P1, sl.P2, sl.P3, isAnalog: false);
                break;

            case BlockOp.DlyBossDd3t:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, Math.Clamp(sl.P0, 20f, 800f), sl.P1, 0f, sl.P2, isAnalog: false);
                break;

            case BlockOp.DlyTimeLine:
                if (st.DelayPoolIdx >= 0)
                    ProcessDelay(_delayPool[st.DelayPoolIdx], bufL, bufR, n, sr, Math.Clamp(sl.P0, 30f, 1400f), sl.P1, sl.P2, sl.P3, isAnalog: sl.P4 > 0.5f);
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

    /// <summary>
    /// 모든 비선형 드라이브/디스토션/퍼즈/부스트 페달의 4x HIIR 오버샘플링 실제 회로도 실행 엔진 (RT 할당 0).
    /// </summary>
    private void ProcessNonlinearPedal4x(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ref CompiledSlot sl)
    {
        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]);

        int fs4 = _sampleRate * 4;
        var buf4 = st.Os4x.Upsample(_monoTmp.AsSpan(0, n), n, _os2x, _os4x);

        switch (sl.Op)
        {
            case BlockOp.OdTs808:
                SchematicPedalDsp.ProcessTubeScreamer4x(ref st.Circuit, buf4, fs4, sl.P0, isTs9: false);
                break;
            case BlockOp.OdTs9:
                SchematicPedalDsp.ProcessTubeScreamer4x(ref st.Circuit, buf4, fs4, sl.P0, isTs9: true);
                break;
            case BlockOp.OdKlon:
                SchematicPedalDsp.ProcessKlonCentaur4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistRat:
                SchematicPedalDsp.ProcessRat4x(ref st.Circuit, buf4, fs4, sl.P0, isRat2: false);
                break;
            case BlockOp.DistRat2:
                SchematicPedalDsp.ProcessRat4x(ref st.Circuit, buf4, fs4, sl.P0, isRat2: true);
                break;
            case BlockOp.FuzzBigMuff:
                SchematicPedalDsp.ProcessBigMuff4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.OdSd1:
                SchematicPedalDsp.ProcessBossSd14x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistBossDs1:
                SchematicPedalDsp.ProcessBossDs14x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.OdBossBd2:
                SchematicPedalDsp.ProcessBossBd24x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistBossMt2:
                SchematicPedalDsp.ProcessBossHighGain4x(ref st.Circuit, buf4, fs4, sl.P0, isHm2: false);
                break;
            case BlockOp.DistBossHm2:
                SchematicPedalDsp.ProcessBossHighGain4x(ref st.Circuit, buf4, fs4, sl.P0, isHm2: true);
                break;
            case BlockOp.OdPureSky:
                SchematicPedalDsp.ProcessTimmyJanRay4x(ref st.Circuit, buf4, fs4, sl.P0, sl.P1, 0f, isJanRay: false);
                break;
            case BlockOp.OdJanRay:
                SchematicPedalDsp.ProcessTimmyJanRay4x(ref st.Circuit, buf4, fs4, sl.P0, sl.P1, sl.P3, isJanRay: true);
                break;
            case BlockOp.OdOcd:
                SchematicPedalDsp.ProcessFulltoneOcd4x(ref st.Circuit, buf4, fs4, sl.P0, highPeak: sl.P3 > 0.5f);
                break;
            case BlockOp.OdBbPreamp:
                SchematicPedalDsp.ProcessBbPreamp4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistEcstasy:
                SchematicPedalDsp.ProcessBognerEcstasy4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.BoostEp:
                SchematicPedalDsp.ProcessCleanBoost4x(ref st.Circuit, buf4, fs4, sl.P0, kind: 0);
                break;
            case BlockOp.BoostRc:
                SchematicPedalDsp.ProcessCleanBoost4x(ref st.Circuit, buf4, fs4, sl.P0, kind: 1);
                break;
            case BlockOp.BoostMicroAmp:
                SchematicPedalDsp.ProcessCleanBoost4x(ref st.Circuit, buf4, fs4, sl.P0, kind: 2);
                break;
            case BlockOp.OdBluesbreaker:
                SchematicPedalDsp.ProcessBluesbreaker4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistGuvnor:
                SchematicPedalDsp.ProcessGuvnor4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.FuzzFace:
                SchematicPedalDsp.ProcessFuzzFace4x(ref st.Circuit, buf4, fs4, sl.P0, sl.P1, isSilicon: sl.P3 > 0.5f);
                break;
            case BlockOp.BoostRangemaster:
                SchematicPedalDsp.ProcessRangemaster4x(ref st.Circuit, buf4, fs4, sl.P0, fullRangeCap: sl.P1 > 0.5f);
                break;
            case BlockOp.DistPlus:
                SchematicPedalDsp.ProcessDistortionPlus4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.OdBoxOfRock:
                SchematicPedalDsp.ProcessBoxOfRock4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
        }

        st.Os4x.Downsample(_monoTmp.AsSpan(0, n), n, _os2x, _os4x);

        // 1x 도메인 각 페달 고유 회로도 출력 톤스택 + 스무딩 아웃풋 레벨 (지퍼 노이즈 차단)
        float alpha = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.005f * _sampleRate));
        float targetOutG = sl.Op switch
        {
            BlockOp.OdTs808 or BlockOp.OdSd1 => 0.25f + 1.65f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdTs9 => 0.25f + 1.75f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdKlon => 0.35f + 1.90f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistRat => 0.22f + 1.50f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistRat2 => 0.22f + 1.55f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.FuzzBigMuff => 0.28f + 1.85f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistBossDs1 => 0.30f + 1.95f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdBossBd2 => 0.28f + 1.75f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistBossMt2 => 0.20f + 1.45f * (Math.Clamp(sl.P5, 0f, 10f) / 10f),
            BlockOp.DistBossHm2 => 0.20f + 1.40f * (Math.Clamp(sl.P3, 0f, 10f) / 10f),
            BlockOp.OdPureSky => 0.30f + 1.85f * (Math.Clamp(sl.P3, 0f, 10f) / 10f),
            BlockOp.OdJanRay => 0.30f + 1.85f * (Math.Clamp(sl.P4, 0f, 10f) / 10f),
            BlockOp.OdOcd => 0.26f + 1.75f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdBbPreamp => 0.28f + 1.75f * (Math.Clamp(sl.P3, 0f, 10f) / 10f),
            BlockOp.DistEcstasy => 0.22f + 1.55f * (Math.Clamp(sl.P4, 0f, 10f) / 10f),
            BlockOp.BoostRc => 0.45f + 2.40f * (Math.Clamp(sl.P1, 0f, 10f) / 10f),
            BlockOp.OdBluesbreaker => 0.32f + 1.90f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistGuvnor => 0.24f + 1.65f * (Math.Clamp(sl.P4, 0f, 10f) / 10f),
            BlockOp.FuzzFace => 0.26f + 1.60f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.BoostRangemaster => 0.90f,
            BlockOp.DistPlus => 0.26f + 1.65f * (Math.Clamp(sl.P1, 0f, 10f) / 10f),
            BlockOp.OdBoxOfRock => 0.28f + 1.80f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            _ => 1.0f,
        };
        if (!st.SmoothReady)
        {
            st.SmoothOutGain = targetOutG;
            st.SmoothReady = true;
        }

        switch (sl.Op)
        {
            case BlockOp.OdTs808:
            case BlockOp.OdTs9:
            {
                // 액티브 TS 톤스택: 패시브 LPF(F1L) + 액티브 하이셸프(F2L)
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F2L.Process(st.F1L.Process(_monoTmp[i])) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.OdSd1:
            case BlockOp.DistRat:
            case BlockOp.OdBluesbreaker:
            case BlockOp.FuzzFace:
            case BlockOp.BoostRangemaster:
            case BlockOp.DistPlus:
            case BlockOp.OdBoxOfRock:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F1L.Process(_monoTmp[i]) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.OdKlon:
            case BlockOp.DistRat2:
            case BlockOp.OdBossBd2:
            case BlockOp.OdPureSky:
            case BlockOp.OdOcd:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F2L.Process(st.F1L.Process(_monoTmp[i])) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.FuzzBigMuff:
            case BlockOp.DistBossDs1:
            {
                // 실제 회로도 병렬 RC 크로스페이드 패시브 톤스택:
                // y = (1 - t) * LPF(x) + t * HPF(x)  -> 중앙(t=0.5)에서 자연 미드 스쿱, 양 끝(0/1)에서 스쿱 소멸!
                float t = Math.Clamp(sl.P1, 0f, 10f) / 10f;
                float lpW = 1f - t;
                float hpW = t;
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float x = _monoTmp[i];
                    float lp = st.F0L.Process(x);
                    float hp = st.F1L.Process(x);
                    float mixed = lp * lpW + hp * hpW;
                    float y = st.F2L.Process(mixed) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.DistBossMt2:
            case BlockOp.DistBossHm2:
            case BlockOp.DistEcstasy:
            case BlockOp.DistGuvnor:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F4L.Process(st.F3L.Process(st.F2L.Process(st.F1L.Process(_monoTmp[i])))) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.OdJanRay:
            case BlockOp.OdBbPreamp:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F3L.Process(st.F2L.Process(st.F1L.Process(_monoTmp[i]))) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.BoostEp:
            case BlockOp.BoostMicroAmp:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F0L.Process(_monoTmp[i]) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.BoostRc:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F1L.Process(st.F0L.Process(_monoTmp[i])) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
        }
    }

    // ── DSP Implementations (All Zero-Allocation) ──

    private static void ProcessGate(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float threshDb, float decayMs, float holdMs)
    {
        if (threshDb <= -78f) return;
        float threshOpen = MathF.Pow(10f, Math.Clamp(threshDb, -80f, -15f) / 20f);
        float threshClose = threshOpen * 0.5623413f;
        int holdMax = Math.Max(32, (int)(Math.Clamp(holdMs, 10f, 400f) * 0.001f * sr));
        float envAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0005f * sr));
        float envRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.050f * sr));
        float gainAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0012f * sr));
        float gainRel = 1f - MathF.Exp(-1f / MathF.Max(1f, Math.Clamp(decayMs, 20f, 500f) * 0.001f * sr));

        for (int i = 0; i < n; i++)
        {
            float a = MathF.Max(MathF.Abs(bufL[i]), MathF.Abs(bufR[i]));
            st.Env += (a > st.Env ? envAtk : envRel) * (a - st.Env);
            if (st.Env >= threshOpen)
            {
                st.GateOpen = true;
                st.HoldCount = holdMax;
            }
            else if (st.GateOpen)
            {
                if (st.Env >= threshClose) st.HoldCount = holdMax;
                else if (st.HoldCount > 0) st.HoldCount--;
                else st.GateOpen = false;
            }
            float target = st.GateOpen ? 1f : 0.006f;
            st.Gain += (target > st.Gain ? gainAtk : gainRel) * (target - st.Gain);
            bufL[i] *= st.Gain;
            bufR[i] *= st.Gain;
        }
    }

    private static void ProcessCompressor(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float sens, float outLvl, float atkMs, float mixPct, bool is1176, float ratio = 4f)
    {
        float sNorm = Math.Clamp(sens, 0f, 10f) / 10f;
        float thresh = MathF.Pow(10f, (-6f - 26f * sNorm) / 20f);
        float compRatio = is1176 ? ratio : (2.5f + 5.5f * sNorm);
        float slope = 1f - 1f / compRatio;
        float makeup = (0.5f + 2.2f * (Math.Clamp(outLvl, 0f, 10f) / 10f)) * (1f + sNorm * 0.75f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, atkMs * 0.001f * sr));
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, (is1176 ? 0.065f : 0.140f) * sr));

        for (int i = 0; i < n; i++)
        {
            float dL = bufL[i], dR = bufR[i];
            float peak = MathF.Max(MathF.Abs(dL), MathF.Abs(dR));
            st.Env += (peak > st.Env ? atk : rel) * (peak - st.Env);
            float gr = st.Env > thresh ? MathF.Pow(st.Env / MathF.Max(1e-6f, thresh), -slope) : 1f;
            float g = gr * makeup;
            bufL[i] = dL * (1f - mix) + dL * g * mix;
            bufR[i] = dR * (1f - mix) + dR * g * mix;
        }
    }

    private void ProcessWhiteBoxAmp(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ToneProcessor host, ref CompiledSlot sl)
    {
        // 독립 듀얼-앰프 풀에서 슬롯 전용 엔진 선택 (듀얼 앰프 병렬 시 상태 충돌 0)
        var amp = (st.AmpPoolIdx >= 0 && st.AmpPoolIdx < _ampPool.Length) ? _ampPool[st.AmpPoolIdx] : host.WhiteBox;

        WhiteBoxChannel ch = sl.SubModel switch
        {
            0 or 1 => WhiteBoxChannel.Clean,
            2 or 3 or 6 or 7 => WhiteBoxChannel.Plexi, // Plexi, 2203, AC30, JCM2000 DSL
            _ => WhiteBoxChannel.HighGain,             // SLO-100, 5153, Recto, BE-100
        };
        bool bright = sl.P7 > 0.5f;
        float depth = sl.SubModel is 4 or 5 or 8 or 9 ? 6.2f : 5.0f;

        amp.ApplyChainAmpParams(
            ch,
            bright,
            preGain: sl.P0,
            bass: sl.P1,
            mid: sl.P2,
            treble: sl.P3,
            master: sl.P4,
            sag: sl.P5,
            presence: sl.P6,
            depth: depth);

        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]);

        amp.ProcessFullAmp(_monoTmp.AsSpan(0, n), n, includePowerAmp: true);

        bool hasContour = sl.SubModel >= 6;
        for (int i = 0; i < n; i++)
        {
            float v = hasContour ? st.F1L.Process(st.F0L.Process(_monoTmp[i])) : _monoTmp[i];
            bufL[i] = v;
            bufR[i] = v;
        }
    }

    private void ProcessNamBlock(Span<float> bufL, Span<float> bufR, int n, ToneProcessor host, float inputDb, float outputDb, float mixPct)
    {
        float inGain = MathF.Pow(10f, Math.Clamp(inputDb, -12f, 12f) / 20f);
        float outGain = MathF.Pow(10f, Math.Clamp(outputDb, -12f, 12f) / 20f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;

        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]) * inGain;

        if (!host.ProcessWaveNetRaw(_monoTmp.AsSpan(0, n), _monoTmp.AsSpan(0, n)))
            return;

        for (int i = 0; i < n; i++)
        {
            float w = _monoTmp[i] * outGain;
            bufL[i] = bufL[i] * (1f - mix) + w * mix;
            bufR[i] = bufR[i] * (1f - mix) + w * mix;
        }
    }

    private void ProcessCabBlock(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ToneProcessor host, byte subModel, float levelDb)
    {
        float targetGain = MathF.Pow(10f, Math.Clamp(levelDb, -12f, 12f) / 20f);
        float alpha = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.005f * _sampleRate));
        if (!st.SmoothReady) { st.SmoothOutGain = targetGain; st.SmoothReady = true; }

        // 7종 고유 512-tap Minimum-Phase IR 또는 외부 Custom WAV IR 선택
        float[]? cabRev = subModel == 1
            ? (host.GetCustomOrProfileCabIrRev() ?? _cabBank.GetIrRev(0))
            : _cabBank.GetIrRev(subModel);

        if (cabRev is { Length: > 0 } && st.CabPoolIdx >= 0 && st.CabPoolIdx < _cabPool.Length)
        {
            // 독립 슬롯 스테레오 SIMD 컨볼루션 (듀얼 캐비넷 충돌 0 + 전단 스테레오 코러스 100% 보존)
            _cabPool[st.CabPoolIdx].ConvolveStereo(bufL, bufR, n, cabRev);
        }

        for (int i = 0; i < n; i++)
        {
            st.SmoothOutGain += alpha * (targetGain - st.SmoothOutGain);
            bufL[i] = st.F1L.Process(st.F0L.Process(bufL[i])) * st.SmoothOutGain;
            bufR[i] = st.F1R.Process(st.F0R.Process(bufR[i])) * st.SmoothOutGain;
        }
    }

    private static void ProcessEq5(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, float levelDb)
    {
        float gain = MathF.Pow(10f, Math.Clamp(levelDb, -12f, 12f) / 20f);
        for (int i = 0; i < n; i++)
        {
            float l = st.F4L.Process(st.F3L.Process(st.F2L.Process(st.F1L.Process(st.F0L.Process(bufL[i]))))) * gain;
            float r = st.F4R.Process(st.F3R.Process(st.F2R.Process(st.F1R.Process(st.F0R.Process(bufR[i]))))) * gain;
            bufL[i] = l;
            bufR[i] = r;
        }
    }

    private static void ProcessEqParam(SlotDspState st, Span<float> bufL, Span<float> bufR, int n)
    {
        for (int i = 0; i < n; i++)
        {
            bufL[i] = st.F2L.Process(st.F1L.Process(st.F0L.Process(bufL[i])));
            bufR[i] = st.F2R.Process(st.F1R.Process(st.F0R.Process(bufR[i])));
        }
    }

    private static void ProcessChorus(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float depthPct, float mixPct)
    {
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        float rate = Math.Clamp(rateHz, 0.1f, 6.0f);
        float depthMs = 0.8f + 4.5f * Math.Clamp(depthPct / 100f, 0f, 1f);
        float baseDelayMs = 6.5f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;
        // BOSS CE-2 MN3007 BBD 프리엠퍼시스/디엠퍼시스 + 3.6kHz 안티앨리어싱 LPF
        float bbdLpA = 1f - MathF.Exp(-2f * MathF.PI * 3600f / sr);

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            // BBD Pre-emphasis (고역 부스트 후 BBD 입력)
            st.Circuit.PreEmpL += bbdLpA * (inL - st.Circuit.PreEmpL);
            st.Circuit.PreEmpR += bbdLpA * (inR - st.Circuit.PreEmpR);
            cL[w] = inL + (inL - st.Circuit.PreEmpL) * 0.45f;
            cR[w] = inR + (inR - st.Circuit.PreEmpR) * 0.45f;

            // CE-2 트라이앵글 LFO 근사 (부드러운 아날로그 코러스 스윕)
            float lfoL = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase));
            float lfoR = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase + MathF.PI * 0.5f));
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float dL = (baseDelayMs + depthMs * lfoL) * 0.001f * sr;
            float dR = (baseDelayMs + depthMs * lfoR) * 0.001f * sr;

            float rawL = ReadFrac(cL, w, dL, mask);
            float rawR = ReadFrac(cR, w, dR, mask);

            // BBD De-emphasis + Reconstruction LPF (따뜻한 MN3007 톤)
            st.Circuit.DeEmpL += bbdLpA * (rawL - st.Circuit.DeEmpL);
            st.Circuit.DeEmpR += bbdLpA * (rawR - st.Circuit.DeEmpR);

            bufL[i] = inL * (1f - mix * 0.5f) + st.Circuit.DeEmpL * mix;
            bufR[i] = inR * (1f - mix * 0.5f) + st.Circuit.DeEmpR * mix;

            st.ChorusWrite = (w + 1) & mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadFrac(float[] buf, int writePos, float delaySamps, int mask)
    {
        float rd = writePos - delaySamps;
        int i0 = (int)MathF.Floor(rd);
        float frac = rd - i0;
        float s0 = buf[i0 & mask];
        float s1 = buf[(i0 + 1) & mask];
        return s0 + (s1 - s0) * frac;
    }

    private static void ProcessPhaser(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float depthPct, float fbPct, float mixPct)
    {
        float rate = Math.Clamp(rateHz, 0.1f, 6.0f);
        float depth = Math.Clamp(depthPct, 0f, 100f) / 100f;
        float fb = Math.Clamp(fbPct, 0f, 80f) / 100f * 0.72f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;

        for (int i = 0; i < n; i++)
        {
            float lfo = 0.5f + 0.5f * MathF.Sin(st.LfoPhase);
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float fHz = 240f + depth * 2200f * lfo;
            float t = MathF.Tan(MathF.PI * MathF.Min(fHz, sr * 0.4f) / sr);
            float a = (1f - t) / (1f + t);

            float inL = bufL[i];
            float yL = inL + st.PhaserFbL * fb;
            yL = Allpass1(yL, a, ref st.Ap0L);
            yL = Allpass1(yL, a, ref st.Ap1L);
            yL = Allpass1(yL, a, ref st.Ap2L);
            yL = Allpass1(yL, a, ref st.Ap3L);
            st.PhaserFbL = yL;
            bufL[i] = inL * (1f - mix * 0.5f) + yL * mix;

            float inR = bufR[i];
            float yR = inR + st.PhaserFbR * fb;
            yR = Allpass1(yR, a, ref st.Ap0R);
            yR = Allpass1(yR, a, ref st.Ap1R);
            yR = Allpass1(yR, a, ref st.Ap2R);
            yR = Allpass1(yR, a, ref st.Ap3R);
            st.PhaserFbR = yR;
            bufR[i] = inR * (1f - mix * 0.5f) + yR * mix;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Allpass1(float x, float a, ref float z)
    {
        float y = -a * x + z;
        z = x + a * y;
        return y;
    }

    private static void ProcessTremolo(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float depthPct, float wave)
    {
        float rate = Math.Clamp(rateHz, 0.5f, 12.0f);
        float depth = Math.Clamp(depthPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;
        bool square = wave > 0.5f;
        // Fender Blackface CdS 옵토커플러: 빠른 점등(attack 4ms), 느린 잔광(release 28ms)
        float optoAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.004f * sr));
        float optoRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.028f * sr));

        for (int i = 0; i < n; i++)
        {
            float s = MathF.Sin(st.LfoPhase);
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;
            float lamp = square ? (s >= 0f ? 1f : 0f) : (0.5f + 0.5f * s);
            st.Circuit.OptoEnv += (lamp > st.Circuit.OptoEnv ? optoAtk : optoRel) * (lamp - st.Circuit.OptoEnv);
            float g = 1f - depth + depth * st.Circuit.OptoEnv;
            bufL[i] *= g;
            bufR[i] *= g;
        }
    }

    private static void ProcessDelay(StereoDelayLine dl, Span<float> bufL, Span<float> bufR, int n, int sr, float timeMs, float fbPct, float toneOrMod, float mixPct, bool isAnalog)
    {
        var bL = dl.BufL;
        var bR = dl.BufR;
        int len = bL.Length;
        if (len == 0) return;

        float delaySampsL = Math.Clamp(timeMs, 30f, 1400f) * 0.001f * sr;
        float delaySampsR = isAnalog ? delaySampsL : Math.Clamp(delaySampsL * 0.75f, 30f * 0.001f * sr, len - 4);
        float fb = Math.Clamp(fbPct, 0f, 88f) / 100f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float lpAlpha = isAnalog
            ? 1f - MathF.Exp(-2f * MathF.PI * (900f + 3800f * Math.Clamp(toneOrMod / 100f, 0f, 1f)) / sr)
            : 1f - MathF.Exp(-2f * MathF.PI * 8500f / sr);
        float modDepth = isAnalog ? 0f : Math.Clamp(toneOrMod / 100f, 0f, 1f) * 0.0018f * sr;
        float modInc = 2f * MathF.PI * 0.8f / sr;

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = dl.WritePos;

            float mod = modDepth > 0f ? MathF.Sin(dl.ModPhase) * modDepth : 0f;
            dl.ModPhase += modInc;
            if (dl.ModPhase > 2f * MathF.PI) dl.ModPhase -= 2f * MathF.PI;

            float rL = ReadLinear(bL, w, delaySampsL + mod, len);
            float rR = ReadLinear(bR, w, delaySampsR - mod, len);

            dl.LpL += lpAlpha * (rL - dl.LpL);
            dl.LpR += lpAlpha * (rR - dl.LpR);

            float fbL = isAnalog ? FastTanh(dl.LpL * 1.15f) * fb : dl.LpR * fb; // Ping-pong for digital!
            float fbR = isAnalog ? FastTanh(dl.LpR * 1.15f) * fb : dl.LpL * fb;

            bL[w] = inL + fbL;
            bR[w] = inR + fbR;

            bufL[i] = inL + dl.LpL * mix;
            bufR[i] = inR + dl.LpR * mix;

            if (++w >= len) w = 0;
            dl.WritePos = w;
        }
    }

    // ── BOSS Pedals DSP Implementations (All Zero-Allocation) ──

    private static void ProcessBossCs3(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float level, float attack, float sustain)
    {
        float susNorm = Math.Clamp(sustain, 0f, 10f) / 10f;
        float thresh = MathF.Pow(10f, (-10f - 28f * susNorm) / 20f);
        float ratio = 3.0f + 7.0f * susNorm;
        float slope = 1f - 1f / ratio;
        float atkMs = 1.5f + (10f - Math.Clamp(attack, 0f, 10f)) * 4.5f;
        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, atkMs * 0.001f * sr));
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, (0.12f + 0.25f * susNorm) * sr));
        float makeup = (0.45f + 2.0f * (Math.Clamp(level, 0f, 10f) / 10f)) * (1f + susNorm * 0.9f);

        for (int i = 0; i < n; i++)
        {
            float l = bufL[i], r = bufR[i];
            float peak = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            st.Env += (peak > st.Env ? atk : rel) * (peak - st.Env);
            float gr = st.Env > thresh ? MathF.Pow(st.Env / MathF.Max(1e-6f, thresh), -slope) : 1f;
            float g = gr * makeup;
            bufL[i] = st.F0L.Process(l * g);
            bufR[i] = st.F0R.Process(r * g);
        }
    }

    private static void ProcessEq7(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, float levelDb)
    {
        float gain = MathF.Pow(10f, Math.Clamp(levelDb, -15f, 15f) / 20f);
        for (int i = 0; i < n; i++)
        {
            float l = st.F6L.Process(st.F5L.Process(st.F4L.Process(st.F3L.Process(st.F2L.Process(st.F1L.Process(st.F0L.Process(bufL[i]))))))) * gain;
            float r = st.F6R.Process(st.F5R.Process(st.F4R.Process(st.F3R.Process(st.F2R.Process(st.F1R.Process(st.F0R.Process(bufR[i]))))))) * gain;
            bufL[i] = l;
            bufR[i] = r;
        }
    }

    private static void ProcessBossDc2w(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float mode1to4, float mixPct)
    {
        // BOSS DC-2W Dimension C: 역위상 듀얼 MN3007 BBD + 컴팬더 프리/디엠퍼시스 + 모노 합산 위상 안전 설계
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        int mode = Math.Clamp((int)MathF.Round(mode1to4), 1, 4);
        float rateHz = mode switch { 1 => 0.45f, 2 => 0.65f, 3 => 0.90f, _ => 1.25f };
        float depthMs = mode switch { 1 => 1.1f, 2 => 1.8f, 3 => 2.6f, _ => 3.5f };
        float baseMs = 5.2f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float inc = 2f * MathF.PI * rateHz / sr;
        float bbdLpA = 1f - MathF.Exp(-2f * MathF.PI * 4200f / sr);

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            st.Circuit.PreEmpL += bbdLpA * (inL - st.Circuit.PreEmpL);
            st.Circuit.PreEmpR += bbdLpA * (inR - st.Circuit.PreEmpR);
            cL[w] = inL + (inL - st.Circuit.PreEmpL) * 0.35f;
            cR[w] = inR + (inR - st.Circuit.PreEmpR) * 0.35f;

            float tri = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase));
            st.LfoPhase += inc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float d1 = (baseMs + depthMs * tri) * 0.001f * sr;
            float d2 = (baseMs - depthMs * tri) * 0.001f * sr;

            float raw1 = ReadFrac(cL, w, d1, mask);
            float raw2 = ReadFrac(cR, w, d2, mask);

            st.Circuit.DeEmpL += bbdLpA * (raw1 - st.Circuit.DeEmpL);
            st.Circuit.DeEmpR += bbdLpA * (raw2 - st.Circuit.DeEmpR);

            // 동위상 크로스-믹스(w1 + 0.25*w2 / w2 + 0.25*w1) + 역방향 LFO:
            // 스테레오에서는 풍성한 3D 공간감, 모노 합산 시에도 위상 상쇄 없음!
            bufL[i] = inL * (1f - mix * 0.35f) + (st.Circuit.DeEmpL * 0.85f + st.Circuit.DeEmpR * 0.15f) * mix;
            bufR[i] = inR * (1f - mix * 0.35f) + (st.Circuit.DeEmpR * 0.85f + st.Circuit.DeEmpL * 0.15f) * mix;

            st.ChorusWrite = (w + 1) & mask;
        }
    }

    private static void ProcessBossBf2(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float manual, float depthPct, float rateHz, float resPct)
    {
        // BOSS BF-2 Flanger: short BBD sweep (0.6ms .. 9ms) with resonant feedback
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        float baseMs = 0.7f + (Math.Clamp(manual, 0f, 10f) / 10f) * 4.5f;
        float depthMs = 0.3f + (Math.Clamp(depthPct, 0f, 100f) / 100f) * 3.8f;
        float rate = Math.Clamp(rateHz, 0.08f, 6.0f);
        float res = (Math.Clamp(resPct, 0f, 90f) / 100f) * 0.78f;
        float inc = 2f * MathF.PI * rate / sr;

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            float lfo = 0.5f + 0.5f * MathF.Sin(st.LfoPhase);
            st.LfoPhase += inc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float dSamps = (baseMs + depthMs * lfo) * 0.001f * sr;
            float flL = ReadFrac(cL, w, dSamps, mask);
            float flR = ReadFrac(cR, w, dSamps * 1.06f, mask);

            cL[w] = inL + FastTanh(flL * 1.1f) * res;
            cR[w] = inR + FastTanh(flR * 1.1f) * res;

            bufL[i] = inL * 0.65f + flL * 0.65f;
            bufR[i] = inR * 0.65f + flR * 0.65f;

            st.ChorusWrite = (w + 1) & mask;
        }
    }

    private static void ProcessJc120(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, float volKnob, float masterKnob)
    {
        // Roland JC-120 Jazz Chorus: 진정한 스테레오 L/R 솔리드스테이트 하이-헤드룸 스튜디오 앰프
        float v = 0.55f + 2.2f * (Math.Clamp(volKnob, 0f, 10f) / 10f);
        float m = 0.35f + 1.45f * (Math.Clamp(masterKnob, 0f, 10f) / 10f);
        for (int i = 0; i < n; i++)
        {
            float xL = st.F3L.Process(bufL[i]) * v;
            float eqL = st.F2L.Process(st.F1L.Process(st.F0L.Process(xL)));
            bufL[i] = FastTanh(eqL * 0.55f) / 0.55f * m;

            float xR = st.F3R.Process(bufR[i]) * v;
            float eqR = st.F2R.Process(st.F1R.Process(st.F0R.Process(xR)));
            bufR[i] = FastTanh(eqR * 0.55f) / 0.55f * m;
        }
    }

    // ── Public Schematic Modulation / Wah / Compressor DSP (Zero-Allocation) ──

    private static void ProcessOrangeSqueezer(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float outputKnob, float biasOrSquash, float mixPct)
    {
        // Dan Armstrong Orange Squeezer: 2N5457 JFET + 4558 반파 Ge 정류 전압더블러 컴프레서
        float squash = 0.3f + 0.7f * (Math.Clamp(biasOrSquash, 0f, 10f) / 10f);
        float thresh = MathF.Pow(10f, (-12f - 22f * squash) / 20f);
        float compSlope = 1f - 1f / (4.5f + 5.5f * squash);
        float outG = 0.50f + 2.15f * (Math.Clamp(outputKnob, 0f, 10f) / 10f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;

        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0025f * sr)); // 빠른 어택 (2.5ms)
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.095f * sr));  // 펀치감 있는 릴리즈

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            // 반파 정류 엔벨로프 검출 (Orange Squeezer 특유의 비대칭 펌핑)
            float halfRect = MathF.Max(0f, MathF.Max(inL, inR));
            st.Env += (halfRect > st.Env ? atk : rel) * (halfRect - st.Env);
            float gr = st.Env > thresh ? MathF.Pow(st.Env / MathF.Max(1e-6f, thresh), -compSlope) : 1f;

            // 2N5457 JFET 미세 2차 고조파 온기
            float sL = inL * gr * 1.35f;
            float sR = inR * gr * 1.35f;
            float jL = (sL >= 0f ? FastTanh(sL * 0.75f) / 0.75f : FastTanh(sL * 0.92f) / 0.92f);
            float jR = (sR >= 0f ? FastTanh(sR * 0.75f) / 0.75f : FastTanh(sR * 0.92f) / 0.92f);

            float wL = st.F0L.Process(jL) * outG;
            float wR = st.F0R.Process(jR) * outG;
            bufL[i] = inL * (1f - mix) + wL * mix;
            bufR[i] = inR * (1f - mix) + wR * mix;
        }
    }

    private static void ProcessCryBabyWah(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float pedalPct, float envPct, float qKnob, float mixPct)
    {
        // Dunlop Cry Baby GCB-95: 500mH Fasel/Halo 인덕터 자이레이터 공진 피크 (350Hz..2200Hz)
        float pedal = Math.Clamp(pedalPct, 0f, 100f) / 100f;
        float autoSens = Math.Clamp(envPct, 0f, 100f) / 100f;
        float q = 2.2f + 2.8f * (Math.Clamp(qKnob, 0f, 10f) / 10f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;

        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.006f * sr));
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.140f * sr));

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            float absIn = MathF.Max(MathF.Abs(inL), MathF.Abs(inR));
            st.Env += (absIn > st.Env ? atk : rel) * (absIn - st.Env);

            if ((i & 15) == 0)
            {
                float sweep = Math.Clamp(pedal + autoSens * MathF.Min(1f, st.Env * 4.5f), 0f, 1f);
                if (i == 0 || MathF.Abs(sweep - st.Circuit.DynBias1) > 1e-4f)
                {
                    st.Circuit.DynBias1 = sweep;
                    // 지수 주파수 매핑: 360Hz * (2160 / 360)^sweep
                    double fc = 360.0 * Math.Pow(6.0, sweep);
                    st.F0L.SetPeaking(sr, fc, q, 13.5);
                    st.F0R.CopyCoeffsFrom(in st.F0L);
                }
            }

            float wL = st.F0L.Process(inL) * 0.62f;
            float wR = st.F0R.Process(inR) * 0.62f;
            bufL[i] = inL * (1f - mix) + wL * mix;
            bufR[i] = inR * (1f - mix) + wR * mix;
        }
    }

    private static void ProcessSmallClone(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float deepSwitch, float mixPct)
    {
        // EHX Small Clone (4600): MN3007 BBD 코러스 + 3.2kHz Sallen-Key 안티앨리어싱 LPF + Deep 스위치
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        float rate = Math.Clamp(rateHz, 0.1f, 5.5f);
        bool deep = deepSwitch > 0.5f;
        float baseMs = deep ? 6.8f : 4.6f;
        float depthMs = deep ? 4.8f : 2.3f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;
        float bbdLpA = 1f - MathF.Exp(-2f * MathF.PI * 3200f / sr);

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            st.Circuit.PreEmpL += bbdLpA * (inL - st.Circuit.PreEmpL);
            st.Circuit.PreEmpR += bbdLpA * (inR - st.Circuit.PreEmpR);
            cL[w] = inL + (inL - st.Circuit.PreEmpL) * 0.42f;
            cR[w] = inR + (inR - st.Circuit.PreEmpR) * 0.42f;

            float triL = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase));
            float triR = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase + MathF.PI * 0.45f));
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float rawL = ReadFrac(cL, w, (baseMs + depthMs * triL) * 0.001f * sr, mask);
            float rawR = ReadFrac(cR, w, (baseMs + depthMs * triR) * 0.001f * sr, mask);

            st.Circuit.DeEmpL += bbdLpA * (rawL - st.Circuit.DeEmpL);
            st.Circuit.DeEmpR += bbdLpA * (rawR - st.Circuit.DeEmpR);

            bufL[i] = inL * (1f - mix * 0.45f) + st.Circuit.DeEmpL * mix;
            bufR[i] = inR * (1f - mix * 0.45f) + st.Circuit.DeEmpR * mix;
            st.ChorusWrite = (w + 1) & mask;
        }
    }

    private static void ProcessUniVibe(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float speedHz, float intensityPct, float vibratoMode)
    {
        // Shin-ei Uni-Vibe (1968): 4단 비대칭 스태거드 커패시터 페이즈 시프터 + CdS 램프/포토셀 열관성 스로브
        float rate = Math.Clamp(speedHz, 0.2f, 8.0f);
        float intensity = Math.Clamp(intensityPct, 0f, 100f) / 100f;
        bool isVibrato = vibratoMode > 0.5f;
        float phaseInc = 2f * MathF.PI * rate / sr;

        // CdS 램프 열관성 비대칭 응답 (점등 10ms, 잔광 42ms -> Uni-Vibe 특유의 맥박 치는 '스롭' 파형)
        float lampAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.010f * sr));
        float lampRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.042f * sr));

        for (int i = 0; i < n; i++)
        {
            float sine = 0.5f + 0.5f * MathF.Sin(st.LfoPhase);
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            st.Circuit.OptoEnv += (sine > st.Circuit.OptoEnv ? lampAtk : lampRel) * (sine - st.Circuit.OptoEnv);
            float fBase = 160f + intensity * 1450f * st.Circuit.OptoEnv;

            // 4단 스태거드(비균등) 주파수 비율: 0.45x, 0.88x, 1.45x, 2.25x (Phase 90과 다른 보컬형 노치 간격)
            float a0 = CalcAp1(fBase * 0.45f, sr);
            float a1 = CalcAp1(fBase * 0.88f, sr);
            float a2 = CalcAp1(fBase * 1.45f, sr);
            float a3 = CalcAp1(fBase * 2.25f, sr);

            float inL = bufL[i];
            float yL = Allpass1(inL + st.PhaserFbL * 0.25f, a0, ref st.Ap0L);
            yL = Allpass1(yL, a1, ref st.Ap1L);
            yL = Allpass1(yL, a2, ref st.Ap2L);
            yL = Allpass1(yL, a3, ref st.Ap3L);
            st.PhaserFbL = yL;
            bufL[i] = isVibrato ? yL : (inL * 0.52f + yL * 0.52f);

            float inR = bufR[i];
            float yR = Allpass1(inR + st.PhaserFbR * 0.25f, a0, ref st.Ap0R);
            yR = Allpass1(yR, a1, ref st.Ap1R);
            yR = Allpass1(yR, a2, ref st.Ap2R);
            yR = Allpass1(yR, a3, ref st.Ap3R);
            st.PhaserFbR = yR;
            bufR[i] = isVibrato ? yR : (inR * 0.52f + yR * 0.52f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalcAp1(float fHz, int sr)
    {
        float t = MathF.Tan(MathF.PI * MathF.Min(fHz, sr * 0.4f) / sr);
        return (1f - t) / (1f + t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadLinear(float[] buf, int writePos, float delaySamps, int len)
    {
        float rd = writePos - delaySamps;
        while (rd < 0f) rd += len;
        int i0 = (int)rd;
        if (i0 >= len) i0 -= len;
        int i1 = i0 + 1;
        if (i1 >= len) i1 = 0;
        float frac = rd - (int)rd;
        return buf[i0] + (buf[i1] - buf[i0]) * frac;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastTanh(float x)
    {
        if (x < -4.5f) return -1f;
        if (x > 4.5f) return 1f;
        float x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }
}
