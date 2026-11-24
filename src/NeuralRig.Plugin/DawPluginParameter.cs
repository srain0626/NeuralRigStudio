namespace NeuralRigStudio.Plugin;

public enum DawParamId : uint
{
    InputGainDb = 0,
    OutputGainDb = 1,
    GateThresholdDb = 2,
    DriveMultiplier = 3,
    WetMix = 4,
    ActiveScene = 5,
    TempoBpm = 6,
    RoutingMode = 7,
    ParallelMix = 8,

    Slot01Bypass = 9,
    Slot02Bypass = 10,
    Slot03Bypass = 11,
    Slot04Bypass = 12,
    Slot05Bypass = 13,
    Slot06Bypass = 14,
    Slot07Bypass = 15,
    Slot08Bypass = 16,
    Slot09Bypass = 17,
    Slot10Bypass = 18,
    Slot11Bypass = 19,
    Slot12Bypass = 20,
    Slot13Bypass = 21,
    Slot14Bypass = 22,
    Slot15Bypass = 23,
    Slot16Bypass = 24,

    AmpGain = 25,
    AmpBass = 26,
    AmpMid = 27,
    AmpTreble = 28,
    AmpMaster = 29,
    AmpSag = 30,
    AmpPresence = 31,
}

public sealed record DawParamDescriptor(
    uint Id,
    string Name,
    string Module,
    string Unit,
    float MinValue,
    float MaxValue,
    float DefaultValue,
    int StepCount,
    bool IsAutomatable = true);

public struct DawParamEvent
{
    public int SampleOffset;
    public uint ParamId;
    public float NormalizedValue;
}

public static class DawParameterCatalog
{
    public const int ParameterCount = 32;

    public static readonly DawParamDescriptor[] All = BuildCatalog();

    private static DawParamDescriptor[] BuildCatalog()
    {
        var list = new List<DawParamDescriptor>(ParameterCount)
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
        };

        for (int i = 0; i < 16; i++)
        {
            int row = i < 8 ? 1 : 2;
            int col = (i % 8) + 1;
            list.Add(new DawParamDescriptor(
                (uint)DawParamId.Slot01Bypass + (uint)i,
                $"Slot R{row}-{col} Active",
                "Chain Bypass",
                "",
                0f, 1f, 1f, 1));
        }

        list.Add(new((uint)DawParamId.AmpGain, "Amp Gain", "Amp Macro", "", 0f, 10f, 6.2f, 0));
        list.Add(new((uint)DawParamId.AmpBass, "Amp Bass", "Amp Macro", "", 0f, 10f, 5.2f, 0));
        list.Add(new((uint)DawParamId.AmpMid, "Amp Mid", "Amp Macro", "", 0f, 10f, 5.8f, 0));
        list.Add(new((uint)DawParamId.AmpTreble, "Amp Treble", "Amp Macro", "", 0f, 10f, 6.0f, 0));
        list.Add(new((uint)DawParamId.AmpMaster, "Amp Master", "Amp Macro", "", 0f, 10f, 5.5f, 0));
        list.Add(new((uint)DawParamId.AmpSag, "Amp Sag", "Amp Macro", "", 0f, 10f, 3.5f, 0));
        list.Add(new((uint)DawParamId.AmpPresence, "Amp Presence", "Amp Macro", "", 0f, 10f, 5.5f, 0));

        return list.ToArray();
    }

    public static float NormalizedToPlain(uint paramId, float norm01)
    {
        if (paramId >= (uint)All.Length) return 0f;
        var d = All[paramId];
        float n = Math.Clamp(norm01, 0f, 1f);
        if (d.StepCount > 0)
        {
            int step = Math.Clamp((int)MathF.Round(n * d.StepCount), 0, d.StepCount);
            return d.MinValue + (d.MaxValue - d.MinValue) * ((float)step / d.StepCount);
        }
        return d.MinValue + (d.MaxValue - d.MinValue) * n;
    }

    public static float PlainToNormalized(uint paramId, float plain)
    {
        if (paramId >= (uint)All.Length) return 0f;
        var d = All[paramId];
        float span = d.MaxValue - d.MinValue;
        if (span <= 1e-6f) return 0f;
        return Math.Clamp((plain - d.MinValue) / span, 0f, 1f);
    }

    public static string FormatNormalizedValue(uint paramId, float norm01)
    {
        if (paramId >= (uint)All.Length) return "";
        var d = All[paramId];
        float plain = NormalizedToPlain(paramId, norm01);
        return paramId switch
        {
            (uint)DawParamId.ActiveScene => $"Scene {(char)('A' + Math.Clamp((int)MathF.Round(plain), 0, 7))}",
            (uint)DawParamId.RoutingMode => plain >= 0.5f ? "Parallel" : "Serial",
            >= (uint)DawParamId.Slot01Bypass and <= (uint)DawParamId.Slot16Bypass => plain >= 0.5f ? "ON" : "BYPASS",
            _ => string.IsNullOrEmpty(d.Unit) ? $"{plain:F2}" : $"{plain:F1} {d.Unit}",
        };
    }
}
