namespace NeuralRigStudio.Plugin;

public enum DawParamId : uint
{
    // Global & Macro Parameters (0..15)
    InputGainDb = 0,
    OutputGainDb = 1,
    GateThresholdDb = 2,
    DriveMultiplier = 3,
    WetMix = 4,
    ActiveScene = 5,
    TempoBpm = 6,
    RoutingMode = 7,
    ParallelMix = 8,
    AmpGain = 9,
    AmpBass = 10,
    AmpMid = 11,
    AmpTreble = 12,
    AmpMaster = 13,
    AmpSag = 14,
    AmpPresence = 15,

    // 16 Matrix Slots x 9 Params per Slot (16..159)
    Slot01Bypass = 16,
    Slot01Param0 = 17,
    Slot01Param1 = 18,
    Slot01Param2 = 19,
    Slot01Param3 = 20,
    Slot01Param4 = 21,
    Slot01Param5 = 22,
    Slot01Param6 = 23,
    Slot01Param7 = 24,

    Slot02Bypass = 25,
    Slot02Param0 = 26,
    Slot02Param1 = 27,
    Slot02Param2 = 28,
    Slot02Param3 = 29,
    Slot02Param4 = 30,
    Slot02Param5 = 31,
    Slot02Param6 = 32,
    Slot02Param7 = 33,

    Slot03Bypass = 34,
    Slot03Param0 = 35,
    Slot03Param1 = 36,
    Slot03Param2 = 37,
    Slot03Param3 = 38,
    Slot03Param4 = 39,
    Slot03Param5 = 40,
    Slot03Param6 = 41,
    Slot03Param7 = 42,

    Slot04Bypass = 43,
    Slot04Param0 = 44,
    Slot04Param1 = 45,
    Slot04Param2 = 46,
    Slot04Param3 = 47,
    Slot04Param4 = 48,
    Slot04Param5 = 49,
    Slot04Param6 = 50,
    Slot04Param7 = 51,

    Slot05Bypass = 52,
    Slot05Param0 = 53,
    Slot05Param1 = 54,
    Slot05Param2 = 55,
    Slot05Param3 = 56,
    Slot05Param4 = 57,
    Slot05Param5 = 58,
    Slot05Param6 = 59,
    Slot05Param7 = 60,

    Slot06Bypass = 61,
    Slot06Param0 = 62,
    Slot06Param1 = 63,
    Slot06Param2 = 64,
    Slot06Param3 = 65,
    Slot06Param4 = 66,
    Slot06Param5 = 67,
    Slot06Param6 = 68,
    Slot06Param7 = 69,

    Slot07Bypass = 70,
    Slot07Param0 = 71,
    Slot07Param1 = 72,
    Slot07Param2 = 73,
    Slot07Param3 = 74,
    Slot07Param4 = 75,
    Slot07Param5 = 76,
    Slot07Param6 = 77,
    Slot07Param7 = 78,

    Slot08Bypass = 79,
    Slot08Param0 = 80,
    Slot08Param1 = 81,
    Slot08Param2 = 82,
    Slot08Param3 = 83,
    Slot08Param4 = 84,
    Slot08Param5 = 85,
    Slot08Param6 = 86,
    Slot08Param7 = 87,

    Slot09Bypass = 88,
    Slot09Param0 = 89,
    Slot09Param1 = 90,
    Slot09Param2 = 91,
    Slot09Param3 = 92,
    Slot09Param4 = 93,
    Slot09Param5 = 94,
    Slot09Param6 = 95,
    Slot09Param7 = 96,

    Slot10Bypass = 97,
    Slot10Param0 = 98,
    Slot10Param1 = 99,
    Slot10Param2 = 100,
    Slot10Param3 = 101,
    Slot10Param4 = 102,
    Slot10Param5 = 103,
    Slot10Param6 = 104,
    Slot10Param7 = 105,

    Slot11Bypass = 106,
    Slot11Param0 = 107,
    Slot11Param1 = 108,
    Slot11Param2 = 109,
    Slot11Param3 = 110,
    Slot11Param4 = 111,
    Slot11Param5 = 112,
    Slot11Param6 = 113,
    Slot11Param7 = 114,

    Slot12Bypass = 115,
    Slot12Param0 = 116,
    Slot12Param1 = 117,
    Slot12Param2 = 118,
    Slot12Param3 = 119,
    Slot12Param4 = 120,
    Slot12Param5 = 121,
    Slot12Param6 = 122,
    Slot12Param7 = 123,

    Slot13Bypass = 124,
    Slot13Param0 = 125,
    Slot13Param1 = 126,
    Slot13Param2 = 127,
    Slot13Param3 = 128,
    Slot13Param4 = 129,
    Slot13Param5 = 130,
    Slot13Param6 = 131,
    Slot13Param7 = 132,

    Slot14Bypass = 133,
    Slot14Param0 = 134,
    Slot14Param1 = 135,
    Slot14Param2 = 136,
    Slot14Param3 = 137,
    Slot14Param4 = 138,
    Slot14Param5 = 139,
    Slot14Param6 = 140,
    Slot14Param7 = 141,

    Slot15Bypass = 142,
    Slot15Param0 = 143,
    Slot15Param1 = 144,
    Slot15Param2 = 145,
    Slot15Param3 = 146,
    Slot15Param4 = 147,
    Slot15Param5 = 148,
    Slot15Param6 = 149,
    Slot15Param7 = 150,

    Slot16Bypass = 151,
    Slot16Param0 = 152,
    Slot16Param1 = 153,
    Slot16Param2 = 154,
    Slot16Param3 = 155,
    Slot16Param4 = 156,
    Slot16Param5 = 157,
    Slot16Param6 = 158,
    Slot16Param7 = 159,
}

public readonly record struct DawParamDescriptor(
    uint Id,
    string Name,
    string Module,
    string Unit,
    float MinValue,
    float MaxValue,
    float DefaultValue,
    int StepCount)
{
    public bool IsStepped => StepCount > 0;
}

public readonly record struct DawModelParamMeta(
    string Label,
    string Units,
    float MinValue,
    float MaxValue,
    float DefaultValue,
    int StepCount,
    string? OnLabel = null,
    string? OffLabel = null);

public static class DawParameterCatalog
{
    public const int TotalParameterCount = 160;
    public const int GlobalParamCount = 16;
    public const int MatrixSlotCount = 16;
    public const int KnobsPerSlot = 8;
    public const int SlotParamStride = 9; // 1 Bypass + 8 Knobs
    public const int SlotParamBaseId = 16;

    public static readonly DawParamDescriptor[] All = BuildAll();

    public static int ParameterCount => All.Length;

    public static uint SlotBypassParamId(int slotIndex)
        => (uint)(SlotParamBaseId + Math.Clamp(slotIndex, 0, MatrixSlotCount - 1) * SlotParamStride);

    public static uint SlotKnobParamId(int slotIndex, int knobIndex)
        => (uint)(SlotParamBaseId + Math.Clamp(slotIndex, 0, MatrixSlotCount - 1) * SlotParamStride + 1 + Math.Clamp(knobIndex, 0, KnobsPerSlot - 1));

    public static bool TryDecodeSlotParam(uint paramId, out int slotIndex, out bool isBypass, out int knobIndex)
    {
        if (paramId >= SlotParamBaseId && paramId < TotalParameterCount)
        {
            int rel = (int)(paramId - SlotParamBaseId);
            slotIndex = rel / SlotParamStride;
            int offset = rel % SlotParamStride;
            if (offset == 0)
            {
                isBypass = true;
                knobIndex = -1;
                return true;
            }
            isBypass = false;
            knobIndex = offset - 1;
            return true;
        }
        slotIndex = -1;
        isBypass = false;
        knobIndex = -1;
        return false;
    }

    private static DawParamDescriptor[] BuildAll()
    {
        var list = new List<DawParamDescriptor>(TotalParameterCount)
        {
            new((uint)DawParamId.InputGainDb, "Input Trim", "Global", "dB", -18f, 18f, 0f, 0),
            new((uint)DawParamId.OutputGainDb, "Output Gain", "Global", "dB", -18f, 18f, 0f, 0),
            new((uint)DawParamId.GateThresholdDb, "Noise Gate", "Global", "dB", -80f, -20f, -56f, 0),
            new((uint)DawParamId.DriveMultiplier, "Input Drive", "Global", "x", 0.2f, 2.2f, 1.0f, 0),
            new((uint)DawParamId.WetMix, "Wet Mix", "Global", "%", 0f, 100f, 100f, 0),
            new((uint)DawParamId.ActiveScene, "Scene (A-H)", "Scenes", "", 0f, 7f, 0f, 7),
            new((uint)DawParamId.TempoBpm, "Tempo BPM", "Sync", "BPM", 40f, 280f, 120f, 0),
            new((uint)DawParamId.RoutingMode, "Routing Mode", "Chain", "", 0f, 1f, 1f, 1),
            new((uint)DawParamId.ParallelMix, "Parallel Mix", "Chain", "%", 0f, 100f, 42f, 0),
            new((uint)DawParamId.AmpGain, "Amp Gain", "Amp Macro", "", 0f, 10f, 6.2f, 0),
            new((uint)DawParamId.AmpBass, "Amp Bass", "Amp Macro", "", 0f, 10f, 5.2f, 0),
            new((uint)DawParamId.AmpMid, "Amp Mid", "Amp Macro", "", 0f, 10f, 5.8f, 0),
            new((uint)DawParamId.AmpTreble, "Amp Treble", "Amp Macro", "", 0f, 10f, 6.0f, 0),
            new((uint)DawParamId.AmpMaster, "Amp Master", "Amp Macro", "", 0f, 10f, 5.5f, 0),
            new((uint)DawParamId.AmpSag, "Amp Sag", "Amp Macro", "", 0f, 10f, 3.5f, 0),
            new((uint)DawParamId.AmpPresence, "Amp Presence", "Amp Macro", "", 0f, 10f, 5.5f, 0),
        };

        for (int s = 0; s < MatrixSlotCount; s++)
        {
            string modName = $"Slot {s + 1:D2}";
            list.Add(new(
                SlotBypassParamId(s),
                $"Slot {s + 1:D2} Active",
                modName,
                "",
                0f,
                1f,
                1f,
                1));

            for (int k = 0; k < KnobsPerSlot; k++)
            {
                list.Add(new(
                    SlotKnobParamId(s, k),
                    $"Slot {s + 1:D2} Knob {k + 1}",
                    modName,
                    "",
                    0f,
                    10f,
                    5.0f,
                    0));
            }
        }

        return list.ToArray();
    }

    public static DawModelParamMeta GetModelParamMeta(string? modelId, int knobIndex)
    {
        int k = Math.Clamp(knobIndex, 0, KnobsPerSlot - 1);
        if (string.IsNullOrWhiteSpace(modelId))
            return new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0);

        string id = modelId.Trim().ToLowerInvariant();

        if (id.StartsWith("amp_", StringComparison.Ordinal))
        {
            return k switch
            {
                0 => new(id == "amp_jc120" ? "Volume" : "Gain", "", 0f, 10f, 5.5f, 0),
                1 => new("Bass", "", 0f, 10f, 5.0f, 0),
                2 => new("Mid", "", 0f, 10f, 5.5f, 0),
                3 => new("Treble", "", 0f, 10f, 5.8f, 0),
                4 => new("Master", "", 0f, 10f, 5.5f, 0),
                5 => new("Sag", "", 0f, 10f, 3.5f, 0),
                6 => new("Presence", "", 0f, 10f, 5.2f, 0),
                7 => new("Bright", "", 0f, 1f, 0f, 1, "On", "Off"),
                _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
            };
        }

        if (id == "nam_capture")
        {
            return k switch
            {
                0 => new("Input", "dB", -12f, 12f, 0f, 0),
                1 => new("Output", "dB", -12f, 12f, 0f, 0),
                2 => new("Mix", "%", 0f, 100f, 100f, 0),
                _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
            };
        }

        if (id.StartsWith("cab_", StringComparison.Ordinal))
        {
            return k switch
            {
                0 => new("Low Cut", "Hz", 20f, 350f, 80f, 0),
                1 => new("High Cut", "Hz", 2000f, 16000f, 9500f, 0),
                2 => new("Level", "dB", -12f, 12f, 0f, 0),
                _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
            };
        }

        switch (id)
        {
            case "dist_mt2":
                return k switch
                {
                    0 => new("Dist", "", 0f, 10f, 7.2f, 0),
                    1 => new("High", "dB", -15f, 15f, 2.5f, 0),
                    2 => new("Low", "dB", -15f, 15f, 3.0f, 0),
                    3 => new("Mid", "dB", -15f, 15f, -4.0f, 0),
                    4 => new("Mid Freq", "Hz", 200f, 5000f, 950f, 0),
                    5 => new("Level", "", 0f, 10f, 5.2f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "od_ocd":
                return k switch
                {
                    0 => new("Drive", "", 0f, 10f, 5.4f, 0),
                    1 => new("Tone", "", 0f, 10f, 5.6f, 0),
                    2 => new("Volume", "", 0f, 10f, 5.5f, 0),
                    3 => new("Peak", "", 0f, 1f, 1f, 1, "HP", "LP"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "boost_ep":
                return k switch
                {
                    0 => new("Boost", "", 0f, 10f, 4.0f, 0),
                    1 => new("Bright", "", 0f, 1f, 1f, 1, "On", "Off"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "fuzz_face":
                return k switch
                {
                    0 => new("Fuzz", "", 0f, 10f, 7.5f, 0),
                    1 => new("Bias", "", 0f, 10f, 5.0f, 0),
                    2 => new("Volume", "", 0f, 10f, 5.6f, 0),
                    3 => new("Transistor", "", 0f, 1f, 0f, 1, "Si BC108", "Ge AC128"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "boost_rangemaster":
                return k switch
                {
                    0 => new("Boost", "", 0f, 10f, 6.5f, 0),
                    1 => new("Input Cap", "", 0f, 1f, 0f, 1, "Full 22n", "Treb 4n7"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "dly_dm2w":
                return k switch
                {
                    0 => new("Repeat Rate", "ms", 40f, 800f, 340f, 0),
                    1 => new("Intensity", "%", 0f, 85f, 38f, 0),
                    2 => new("Echo", "%", 0f, 100f, 28f, 0),
                    3 => new("Custom", "", 0f, 1f, 1f, 1, "Custom", "Std"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "dly_dd3t":
                return k switch
                {
                    0 => new("D.Time", "ms", 20f, 800f, 380f, 0),
                    1 => new("F.Back", "%", 0f, 85f, 34f, 0),
                    2 => new("E.Level", "%", 0f, 100f, 26f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "dly_analog":
                return k switch
                {
                    0 => new("Time", "ms", 40f, 1200f, 360f, 0),
                    1 => new("Feedback", "%", 0f, 85f, 36f, 0),
                    2 => new("Tone", "%", 0f, 100f, 48f, 0),
                    3 => new("Mix", "%", 0f, 100f, 26f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "dly_digital":
                return k switch
                {
                    0 => new("Time", "ms", 40f, 1350f, 420f, 0),
                    1 => new("Feedback", "%", 0f, 85f, 34f, 0),
                    2 => new("Mod", "%", 0f, 100f, 18f, 0),
                    3 => new("Mix", "%", 0f, 100f, 24f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "dly_timeline":
                return k switch
                {
                    0 => new("Time", "ms", 40f, 1350f, 430f, 0),
                    1 => new("Repeats", "%", 0f, 85f, 36f, 0),
                    2 => new("Mod / Filter", "%", 0f, 100f, 28f, 0),
                    3 => new("Mix", "%", 0f, 100f, 26f, 0),
                    4 => new("Machine", "", 0f, 1f, 0f, 1, "dTape", "dBucket"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "rev_bluesky":
                return k switch
                {
                    0 => new("Decay", "%", 5f, 100f, 66f, 0),
                    1 => new("Pre-Delay", "ms", 0f, 120f, 22f, 0),
                    2 => new("High Damp", "%", 0f, 100f, 36f, 0),
                    3 => new("Mix", "%", 0f, 100f, 32f, 0),
                    4 => new("Shimmer", "%", 0f, 100f, 28f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "rev_rv6":
                return k switch
                {
                    0 => new("E.Level", "%", 0f, 100f, 28f, 0),
                    1 => new("Tone", "%", 0f, 100f, 58f, 0),
                    2 => new("Time", "%", 5f, 100f, 56f, 0),
                    3 => new("Mode", "", 0f, 3f, 0f, 3),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "rev_spring":
            case "rev_plate":
                return k switch
                {
                    0 => new("Decay", "%", 5f, 100f, 50f, 0),
                    1 => new("Damping", "%", 0f, 100f, 38f, 0),
                    2 => new("Mix", "%", 0f, 100f, 24f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "rev_hall":
                return k switch
                {
                    0 => new("Decay", "%", 5f, 100f, 58f, 0),
                    1 => new("Damping", "%", 0f, 100f, 45f, 0),
                    2 => new("Pre-Delay", "ms", 0f, 120f, 24f, 0),
                    3 => new("Mix", "%", 0f, 100f, 26f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "mod_ce2":
                return k switch
                {
                    0 => new("Rate", "Hz", 0.1f, 5.5f, 1.1f, 0),
                    1 => new("Depth", "%", 0f, 100f, 48f, 0),
                    2 => new("Mix", "%", 0f, 100f, 45f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "mod_dc2w":
                return k switch
                {
                    0 => new("Mode", "", 1f, 4f, 2f, 3),
                    1 => new("Mix", "%", 0f, 100f, 60f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "mod_bf2":
                return k switch
                {
                    0 => new("Manual", "", 0f, 10f, 4.5f, 0),
                    1 => new("Depth", "%", 0f, 100f, 55f, 0),
                    2 => new("Rate", "Hz", 0.1f, 5.5f, 0.65f, 0),
                    3 => new("Res", "%", 0f, 85f, 48f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "mod_phase90":
                return k switch
                {
                    0 => new("Speed", "Hz", 0.1f, 5.5f, 0.8f, 0),
                    1 => new("Depth", "%", 0f, 100f, 65f, 0),
                    2 => new("Feedback", "%", 0f, 80f, 35f, 0),
                    3 => new("Mix", "%", 0f, 100f, 50f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "mod_tremolo":
                return k switch
                {
                    0 => new("Rate", "Hz", 0.5f, 12f, 4.5f, 0),
                    1 => new("Depth", "%", 0f, 100f, 55f, 0),
                    2 => new("Wave", "", 0f, 1f, 0f, 1, "Square", "Sine"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "mod_smallclone":
                return k switch
                {
                    0 => new("Rate", "Hz", 0.1f, 5.5f, 0.85f, 0),
                    1 => new("Depth", "", 0f, 1f, 1f, 1, "Deep", "Normal"),
                    2 => new("Mix", "%", 0f, 100f, 52f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "mod_univibe":
                return k switch
                {
                    0 => new("Speed", "Hz", 0.2f, 8.0f, 1.6f, 0),
                    1 => new("Intensity", "%", 0f, 100f, 72f, 0),
                    2 => new("Mode", "", 0f, 1f, 0f, 1, "Vibrato", "Chorus"),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "eq_crybaby":
                return k switch
                {
                    0 => new("Pedal", "%", 0f, 100f, 52f, 0),
                    1 => new("Auto Env", "%", 0f, 100f, 35f, 0),
                    2 => new("Resonance", "", 0f, 10f, 5.8f, 0),
                    3 => new("Mix", "%", 0f, 100f, 100f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "eq_ge7":
                return k switch
                {
                    0 => new("100 Hz", "dB", -15f, 15f, 0f, 0),
                    1 => new("200 Hz", "dB", -15f, 15f, 0f, 0),
                    2 => new("400 Hz", "dB", -15f, 15f, 0f, 0),
                    3 => new("800 Hz", "dB", -15f, 15f, 0f, 0),
                    4 => new("1.6 kHz", "dB", -15f, 15f, 0f, 0),
                    5 => new("3.2 kHz", "dB", -15f, 15f, 0f, 0),
                    6 => new("6.4 kHz", "dB", -15f, 15f, 0f, 0),
                    7 => new("Level", "dB", -15f, 15f, 0f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "eq_graphic5":
                return k switch
                {
                    0 => new("100 Hz", "dB", -12f, 12f, 0f, 0),
                    1 => new("300 Hz", "dB", -12f, 12f, 0f, 0),
                    2 => new("800 Hz", "dB", -12f, 12f, 0f, 0),
                    3 => new("2.2 kHz", "dB", -12f, 12f, 0f, 0),
                    4 => new("5.5 kHz", "dB", -12f, 12f, 0f, 0),
                    5 => new("Level", "dB", -12f, 12f, 0f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "eq_parametric":
                return k switch
                {
                    0 => new("Low Cut", "Hz", 20f, 350f, 70f, 0),
                    1 => new("Mid Freq", "Hz", 150f, 5000f, 900f, 0),
                    2 => new("Mid Gain", "dB", -12f, 12f, 2.0f, 0),
                    3 => new("Mid Q", "", 0.4f, 6.0f, 1.4f, 0),
                    4 => new("High Cut", "Hz", 2500f, 16000f, 11000f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "gate_adaptive":
                return k switch
                {
                    0 => new("Threshold", "dB", -80f, -20f, -56f, 0),
                    1 => new("Decay", "ms", 20f, 400f, 120f, 0),
                    2 => new("Hold", "ms", 10f, 300f, 80f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "comp_sp":
                return k switch
                {
                    0 => new("Blend", "%", 0f, 100f, 55f, 0),
                    1 => new("Volume", "", 0f, 10f, 5.4f, 0),
                    2 => new("Comp", "", 0f, 2f, 1f, 2),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "comp_orangesqueezer":
                return k switch
                {
                    0 => new("Output", "", 0f, 10f, 5.6f, 0),
                    1 => new("Tone", "", 0f, 10f, 5.4f, 0),
                    2 => new("Bias", "", 0f, 10f, 5.5f, 0),
                    3 => new("Mix", "%", 0f, 100f, 100f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "comp_dyna":
                return k switch
                {
                    0 => new("Sensitivity", "", 0f, 10f, 4.5f, 0),
                    1 => new("Output", "", 0f, 10f, 5.5f, 0),
                    2 => new("Attack", "ms", 1f, 50f, 12f, 0),
                    3 => new("Mix", "%", 0f, 100f, 100f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "comp_1176":
                return k switch
                {
                    0 => new("Input", "", 0f, 10f, 5.0f, 0),
                    1 => new("Output", "", 0f, 10f, 5.2f, 0),
                    2 => new("Ratio", "", 2f, 20f, 4f, 0),
                    3 => new("Mix", "%", 0f, 100f, 85f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "pitch_capo":
                return k switch
                {
                    0 => new("Semitones", "st", -12f, 12f, -2f, 24),
                    1 => new("Detune", "ct", -50f, 50f, 0f, 0),
                    2 => new("Mix", "%", 0f, 100f, 100f, 0),
                    3 => new("Level", "dB", -12f, 12f, 0f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "pitch_whammy":
                return k switch
                {
                    0 => new("Pedal", "%", 0f, 100f, 0f, 0),
                    1 => new("Interval", "st", -24f, 24f, 12f, 48),
                    2 => new("Mix", "%", 0f, 100f, 100f, 0),
                    3 => new("Level", "dB", -12f, 12f, 0f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            case "pitch_octaver":
                return k switch
                {
                    0 => new("Dry", "%", 0f, 100f, 80f, 0),
                    1 => new("Sub -1 Oct", "%", 0f, 100f, 65f, 0),
                    2 => new("Up +1 Oct", "%", 0f, 100f, 45f, 0),
                    3 => new("Tone", "", 0f, 10f, 5.5f, 0),
                    4 => new("Level", "dB", -12f, 12f, 0f, 0),
                    _ => new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0),
                };
            default:
                return new($"Param {k + 1}", "", 0f, 10f, 5.0f, 0);
        }
    }

    public static float NormalizedToPlain(uint paramId, float norm01)
    {
        int idx = (int)paramId;
        if ((uint)idx >= All.Length) return 0f;
        var p = All[idx];
        return NormalizedToPlainRange(norm01, p.MinValue, p.MaxValue, p.StepCount);
    }

    public static float NormalizedToPlainRange(float norm01, float minVal, float maxVal, int stepCount)
    {
        float clamped = Math.Clamp(norm01, 0f, 1f);
        if (stepCount > 0)
        {
            int step = Math.Clamp((int)MathF.Round(clamped * stepCount), 0, stepCount);
            float frac = stepCount > 0 ? (float)step / stepCount : 0f;
            return minVal + frac * (maxVal - minVal);
        }
        return minVal + clamped * (maxVal - minVal);
    }

    public static float PlainToNormalized(uint paramId, float plain)
    {
        int idx = (int)paramId;
        if ((uint)idx >= All.Length) return 0f;
        var p = All[idx];
        return PlainToNormalizedRange(plain, p.MinValue, p.MaxValue);
    }

    public static float PlainToNormalizedRange(float plain, float minVal, float maxVal)
    {
        float span = maxVal - minVal;
        if (span <= 1e-6f) return 0f;
        return Math.Clamp((plain - minVal) / span, 0f, 1f);
    }

    public static string FormatNormalizedValue(uint paramId, float norm01)
    {
        int idx = (int)paramId;
        if ((uint)idx >= All.Length) return "0";
        var p = All[idx];
        float plain = NormalizedToPlain(paramId, norm01);
        return FormatPlainValue(paramId, plain, p.Unit, null, null);
    }

    public static string FormatPlainValue(uint paramId, float plain, string units, string? onLabel, string? offLabel)
    {
        if (paramId == (uint)DawParamId.ActiveScene)
            return $"Scene {(char)('A' + Math.Clamp((int)MathF.Round(plain), 0, 7))}";
        if (paramId == (uint)DawParamId.RoutingMode)
            return plain >= 0.5f ? "Parallel" : "Serial";

        if (TryDecodeSlotParam(paramId, out _, out bool isBypass, out _))
        {
            if (isBypass)
                return plain >= 0.5f ? "ON" : "BYPASS";
            if (!string.IsNullOrEmpty(onLabel) && !string.IsNullOrEmpty(offLabel))
                return plain >= 0.5f ? onLabel : offLabel;
        }

        string num = units switch
        {
            "Hz" or "ms" or "%" or "st" or "ct" => $"{plain:0}",
            _ => $"{plain:0.0}",
        };
        return string.IsNullOrEmpty(units) ? num : $"{num} {units}";
    }
}
