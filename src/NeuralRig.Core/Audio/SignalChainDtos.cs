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
    Pitch = 10,
}

public enum ChainRoutingMode
{
    Serial = 0,        // Row 1 (0..7) -> Row 2 (8..15) -> Out
    ParallelSplit = 1, // Row 1 splits at SplitCol into Row 2, merges at MergeCol
}

/// <summary>
/// 글로벌 BPM 대비 슬롯(딜레이 타임 / 모듈레이션 LFO) 음표 박자 동기화 모드.
/// </summary>
public enum TempoSyncDivision
{
    Free = 0,          // 노브 값(ms 또는 Hz) 직접 사용
    Quarter = 1,       // 1/4 정박
    DottedEighth = 2,  // 1/8. 점8분음표 (0.75박)
    Eighth = 3,        // 1/8 8분음표 (0.5박)
    EighthTriplet = 4, // 1/8T 셋잇단음표 (1/3박)
    Sixteenth = 5,     // 1/16 16분음표 (0.25박)
}

/// <summary>
/// 가상 캐비넷 룸 듀얼 마이킹(Dual Virtual Miking) 설정 DTO.
/// </summary>
public sealed class CabMicConfigDto
{
    public bool DualEnabled { get; set; }
    public int MicAType { get; set; } = 0;       // 0=SM57, 1=R121, 2=C414, 3=MD421, 4=M160, 5=SM7B
    public float MicAX { get; set; } = 0.25f;    // 0(Cap) .. 1(Edge)
    public float MicADist { get; set; } = 0.20f; // 0(Close) .. 1(Far)
    public float MicALevelDb { get; set; } = 0f; // -18 .. +6 dB
    public float MicAPan { get; set; } = -0.35f; // -1 .. +1
    public bool MicAPhaseInv { get; set; }

    public int MicBType { get; set; } = 1;
    public float MicBX { get; set; } = 0.55f;
    public float MicBDist { get; set; } = 0.35f;
    public float MicBLevelDb { get; set; } = -1.5f;
    public float MicBPan { get; set; } = 0.35f;
    public bool MicBPhaseInv { get; set; }
    public float MicBDelayMs { get; set; } = 0f; // 0 .. 15 ms (Haas)
    public float Blend { get; set; } = 0.45f;    // 0(MicA) .. 1(MicB)

    public CabMicConfigDto Clone() => new()
    {
        DualEnabled = DualEnabled,
        MicAType = Math.Clamp(MicAType, 0, 5),
        MicAX = Math.Clamp(MicAX, 0f, 1f),
        MicADist = Math.Clamp(MicADist, 0f, 1f),
        MicALevelDb = Math.Clamp(MicALevelDb, -18f, 6f),
        MicAPan = Math.Clamp(MicAPan, -1f, 1f),
        MicAPhaseInv = MicAPhaseInv,
        MicBType = Math.Clamp(MicBType, 0, 5),
        MicBX = Math.Clamp(MicBX, 0f, 1f),
        MicBDist = Math.Clamp(MicBDist, 0f, 1f),
        MicBLevelDb = Math.Clamp(MicBLevelDb, -18f, 6f),
        MicBPan = Math.Clamp(MicBPan, -1f, 1f),
        MicBPhaseInv = MicBPhaseInv,
        MicBDelayMs = Math.Clamp(MicBDelayMs, 0f, 15f),
        Blend = Math.Clamp(Blend, 0f, 1f),
    };
}

/// <summary>
/// 직렬화 가능한 시그널 체인 단일 슬롯 DTO (UI <-> C# 브리지 <-> settings.json).
/// </summary>
public sealed class ChainSlotDto
{
    public int Index { get; set; }
    public string ModelId { get; set; } = "";
    public string? NamModelId { get; set; }
    public bool Enabled { get; set; } = true;
    public int SyncDivision { get; set; } = 0; // 0..5 (TempoSyncDivision)
    public float[] Params { get; set; } = new float[8];
    public CabMicConfigDto? CabMic { get; set; }

    public ChainSlotDto Clone()
    {
        var p = new float[8];
        if (Params != null)
            Array.Copy(Params, p, Math.Min(8, Params.Length));
        return new ChainSlotDto
        {
            Index = Index,
            ModelId = ModelId ?? "",
            NamModelId = string.IsNullOrWhiteSpace(NamModelId) ? null : NamModelId.Trim(),
            Enabled = Enabled,
            SyncDivision = Math.Clamp(SyncDivision, 0, 5),
            Params = p,
            CabMic = CabMic?.Clone(),
        };
    }
}

/// <summary>
/// 개별 씬(A..H)의 슬롯 ON/OFF 및 파라미터 스냅샷 DTO.
/// </summary>
public sealed class SceneSlotStateDto
{
    public bool Enabled { get; set; } = true;
    public int SyncDivision { get; set; } = 0;
    public float[] Params { get; set; } = new float[8];

    public SceneSlotStateDto Clone()
    {
        var p = new float[8];
        if (Params != null) Array.Copy(Params, p, Math.Min(8, Params.Length));
        return new SceneSlotStateDto
        {
            Enabled = Enabled,
            SyncDivision = Math.Clamp(SyncDivision, 0, 5),
            Params = p,
        };
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
/// 직렬화 가능한 전체 시그널 체인 구성 DTO (2행 × 8슬롯 = 16슬롯 + 8개 씬 스냅샷 + 글로벌 BPM).
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
    public float TempoBpm { get; set; } = 120f;     // 40..280 글로벌 BPM
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
            TempoBpm = TempoBpm > 1f ? Math.Clamp(TempoBpm, 40f, 280f) : 120f,
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
