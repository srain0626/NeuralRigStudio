using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuralRigStudio.Audio;

public enum ToneMode
{
    Bypass = 0,
    FullRig = 1,          // [TS808?] -> NAM WaveNet (Amp+Cab)
    CabIrOnly = 2,        // Cab IR Only
    AmpHeadOnly = 3,      // [TS808?] -> NAM WaveNet -> Cab IR
    WhiteBoxRig = 4,      // 4x OS WhiteBox Circuit -> Cab IR
    HybridPreampNam = 5,  // 4x OS WhiteBox Front-End -> NAM WaveNet -> [CustomCab?]
    SignalChain = 6,      // Quad Cortex Style 2x8 Modular Signal Chain
}

public enum WaveNetActKind
{
    Tanh = 0,
    SiLU = 1,
    LeakyReLU = 2,
    ReLU = 3,
    Sigmoid = 4,
}

public sealed class TrainingLossPoint
{
    public int Step { get; set; }
    public int TotalSteps { get; set; }
    public double TrainEsr { get; set; }
    public double ValEsr { get; set; }
    public double ValEsrDb { get; set; }
}

public sealed class LayerArrayHeadCfg
{
    [JsonPropertyName("out_channels")]
    public int OutChannels { get; set; } = 1;

    [JsonPropertyName("kernel_size")]
    public int KernelSize { get; set; } = 1;

    [JsonPropertyName("bias")]
    public bool Bias { get; set; }
}

public sealed class Layer1x1Cfg
{
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("groups")]
    public int Groups { get; set; } = 1;
}

public sealed class Head1x1Cfg
{
    [JsonPropertyName("active")]
    public bool Active { get; set; } = false;

    [JsonPropertyName("out_channels")]
    public int OutChannels { get; set; } = 1;

    [JsonPropertyName("groups")]
    public int Groups { get; set; } = 1;
}

public sealed class WaveNetLayerArrayCfg
{
    [JsonPropertyName("input_size")]
    public int InputSize { get; set; } = 1;

    [JsonPropertyName("condition_size")]
    public int ConditionSize { get; set; } = 1;

    [JsonPropertyName("head_size")]
    public int HeadSize { get; set; } = 1;

    [JsonPropertyName("head")]
    public LayerArrayHeadCfg? Head { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; } = 12;

    [JsonPropertyName("bottleneck")]
    public int? Bottleneck { get; set; }

    [JsonPropertyName("kernel_size")]
    public int KernelSize { get; set; } = 3;

    [JsonPropertyName("kernel_sizes")]
    public int[]? KernelSizes { get; set; }

    [JsonPropertyName("dilations")]
    public int[] Dilations { get; set; } = Array.Empty<int>();

    [JsonPropertyName("activation")]
    public JsonElement Activation { get; set; } = JsonSerializer.SerializeToElement("Tanh");

    [JsonPropertyName("gated")]
    public bool Gated { get; set; }

    [JsonPropertyName("gating_mode")]
    public string[]? GatingMode { get; set; }

    [JsonPropertyName("head_bias")]
    public bool HeadBias { get; set; }

    [JsonPropertyName("layer1x1")]
    public Layer1x1Cfg? Layer1x1 { get; set; }

    [JsonPropertyName("layer_1x1_config")]
    public Layer1x1Cfg? Layer1x1Alt { get; set; }

    [JsonPropertyName("head1x1")]
    public Head1x1Cfg? Head1x1 { get; set; }

    [JsonPropertyName("head_1x1_config")]
    public Head1x1Cfg? Head1x1Alt { get; set; }
}

public sealed class WaveNetHeadCfg
{
    [JsonPropertyName("in_channels")]
    public int InChannels { get; set; } = 1;

    [JsonPropertyName("channels")]
    public int Channels { get; set; } = 1;

    [JsonPropertyName("out_channels")]
    public int OutChannels { get; set; } = 1;

    [JsonPropertyName("kernel_sizes")]
    public int[] KernelSizes { get; set; } = new[] { 1 };

    [JsonPropertyName("activation")]
    public JsonElement Activation { get; set; } = JsonSerializer.SerializeToElement("Tanh");
}

public sealed class WaveNetConfigDto
{
    [JsonPropertyName("layers")]
    public List<WaveNetLayerArrayCfg> Layers { get; set; } = new();

    [JsonPropertyName("head")]
    public WaveNetHeadCfg? Head { get; set; }

    [JsonPropertyName("head_scale")]
    public float HeadScale { get; set; } = 0.02f;
}

/// <summary>
/// Runtime-ready SIMD-packed WaveNet topology compiled off the real-time audio thread.
/// </summary>
public sealed class CompiledWaveNet
{
    public sealed class Layer
    {
        public int Channels;
        public int PadChannels;
        public int Bottleneck;
        public int MidChannels;
        public int PadMidChannels;
        public int ConditionSize;
        public int KernelSize;
        public int Dilation;
        public int RingCapacity;
        public int RingMask;
        public bool Gated;
        public WaveNetActKind Act;
        public bool Layer1x1Active;
        public bool Head1x1Active;
        public int Head1x1Out;
        public int PadHeadOut;

        // Packed row-major output layouts for zero-HADD FMA SIMD:
        // ConvW[k, ic, oc] -> size: KernelSize * Channels * PadMidChannels
        public float[] ConvW = Array.Empty<float>();
        public float[] ConvB = Array.Empty<float>();
        // InputMixerW[cond, oc] -> size: ConditionSize * PadMidChannels
        public float[] InputMixerW = Array.Empty<float>();
        // Layer1x1W[bn, oc] -> size: Bottleneck * PadChannels
        public float[] Layer1x1W = Array.Empty<float>();
        public float[] Layer1x1B = Array.Empty<float>();
        // Head1x1W[bn, ho] -> size: Bottleneck * PadHeadOut
        public float[] Head1x1W = Array.Empty<float>();
    }

    public sealed class LayerArray
    {
        public int InputSize;
        public int Channels;
        public int PadChannels;
        public int HeadInChannels;
        public int HeadOutChannels;
        public int PadHeadOutChannels;
        public int HeadKernelSize;
        public int HeadRingCapacity;
        public int HeadRingMask;

        // RechannelW[ic, oc] -> InputSize * PadChannels
        public float[] RechannelW = Array.Empty<float>();
        public Layer[] Layers = Array.Empty<Layer>();
        // HeadRechannelW[k, ic, oc] -> HeadKernelSize * HeadInChannels * PadHeadOutChannels
        public float[] HeadRechannelW = Array.Empty<float>();
        public float[] HeadRechannelB = Array.Empty<float>();
    }

    public LayerArray[] Arrays { get; init; } = Array.Empty<LayerArray>();
    public float HeadScale { get; init; } = 0.02f;
    public int MaxChannelsPadded { get; init; }
    public int MaxMidChannelsPadded { get; init; }
    public int MaxHeadBusChannelsPadded { get; init; }
    public int MaxDilation { get; init; }
}

public sealed class ToneProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; } = 3;
    public string Architecture { get; set; } = "WaveNet";
    public string CaptureKind { get; set; } = "pair";
    public string ArchPreset { get; set; } = "feather";
    public int DefaultToneMode { get; set; } = 1;
    public int SampleRate { get; set; } = 44100;
    public string ToneClass { get; set; } = "High-Gain";
    public string GpuDevice { get; set; } = "DirectML";
    public double Loss { get; set; }
    public double Esr { get; set; }
    public double EsrDb { get; set; }
    public double SimilarityPct { get; set; }
    public double SnrDb { get; set; }
    public double LatencySamples { get; set; }
    public double LatencyMs { get; set; }
    public double DriftSamples { get; set; }
    public bool PolarityInverted { get; set; }
    public double WetPeakDb { get; set; }
    public bool HasClipping { get; set; }
    public string CreatedAt { get; set; } = "";
    public string SourceUrl { get; set; } = "";

    // v3 Official NAM WaveNet representation
    public WaveNetConfigDto? WaveNetConfig { get; set; }
    public float[] Weights { get; set; } = Array.Empty<float>();

    // Optional deconvolved / extracted Cab IR (also drives CabIrOnly & Visualizer curve)
    public float[] CabIr { get; set; } = Array.Empty<float>();
    public float[] CabIrRev { get; set; } = Array.Empty<float>();

    // Legacy v1/v2 fields (kept read-compatible)
    public float[] PreEq { get; set; } = Array.Empty<float>();
    public float[] PreEqRev { get; set; } = Array.Empty<float>();
    public float PreGain { get; set; } = 1.0f;
    public float Bias { get; set; } = 0.0f;
    public float NetScale { get; set; } = 0.12f;
    public int[] ConvDilations { get; set; } = new[] { 1, 4, 16 };
    public float[] Conv1W { get; set; } = Array.Empty<float>();
    public float[] Conv1B { get; set; } = Array.Empty<float>();
    public float[] Conv2W { get; set; } = Array.Empty<float>();
    public float[] Conv2B { get; set; } = Array.Empty<float>();
    public float[] Conv3W { get; set; } = Array.Empty<float>();
    public float[] Conv3B { get; set; } = Array.Empty<float>();
    public float CompThresholdDb { get; set; } = -18f;
    public float CompRatio { get; set; } = 3.0f;
    public float StereoWidth { get; set; } = 0.0f;
    public float OutputGain { get; set; } = 1.0f;

    public string AuditionTargetPath { get; set; } = "";
    public string AuditionYtPath { get; set; } = "";
    public string AuditionClonedPath { get; set; } = "";
    public string GuitarStemPath { get; set; } = "";
    public string BackingTrackPath { get; set; } = "";
    public string NamFilePath { get; set; } = "";
    public List<TrainingLossPoint> LossHistory { get; set; } = new();

    [JsonIgnore]
    public string JsonPath { get; set; } = "";

    [JsonIgnore]
    public CompiledWaveNet? CompiledNet { get; private set; }

    [JsonIgnore]
    public int Dilation1 { get; private set; } = 1;
    [JsonIgnore]
    public int Dilation2 { get; private set; } = 4;
    [JsonIgnore]
    public int Dilation3 { get; private set; } = 16;

    [JsonIgnore]
    public bool IsWaveNet => CompiledNet != null;

    [JsonIgnore]
    public bool HasLegacyNeuralNet =>
        Conv1W is { Length: 12 * 1 * 3 } &&
        Conv1B is { Length: 12 } &&
        Conv2W is { Length: 12 * 12 * 3 } &&
        Conv2B is { Length: 12 } &&
        Conv3W is { Length: 1 * 12 * 3 } &&
        Conv3B is { Length: 1 };

    [JsonIgnore]
    public bool HasNeuralNet => IsWaveNet || HasLegacyNeuralNet;

    [JsonIgnore]
    public bool HasPreEq => PreEq is { Length: > 0 and <= 64 };

    [JsonIgnore]
    public bool HasCabIr => CabIr is { Length: > 0 };

    public void PrepareForRt()
    {
        if (PreEq is { Length: > 0 and <= 64 })
        {
            PreEqRev = new float[PreEq.Length];
            for (int i = 0; i < PreEq.Length; i++)
                PreEqRev[i] = PreEq[PreEq.Length - 1 - i];
        }
        else
        {
            PreEqRev = Array.Empty<float>();
        }

        if (CabIr is { Length: > 0 })
        {
            int n = Math.Min(CabIr.Length, 2048);
            CabIrRev = new float[n];
            for (int i = 0; i < n; i++)
                CabIrRev[i] = CabIr[n - 1 - i];
        }
        else
        {
            CabIrRev = Array.Empty<float>();
        }

        Dilation1 = ConvDilations is { Length: >= 1 } && ConvDilations[0] > 0 ? ConvDilations[0] : 1;
        Dilation2 = ConvDilations is { Length: >= 2 } && ConvDilations[1] > 0 ? ConvDilations[1] : 4;
        Dilation3 = ConvDilations is { Length: >= 3 } && ConvDilations[2] > 0 ? ConvDilations[2] : 16;

        CompiledNet = null;
        if (WaveNetConfig != null && WaveNetConfig.Layers is { Count: > 0 } && Weights is { Length: > 0 })
        {
            CompiledNet = CompileWaveNet(WaveNetConfig, Weights);
        }
    }

    private static int Pad8(int n) => Math.Max(8, (n + 7) & ~7);

    private static int NextPow2(int v)
    {
        int p = 4;
        while (p < v) p <<= 1;
        return p;
    }

    private static WaveNetActKind ParseActivation(JsonElement actEl, int layerIdx)
    {
        string? name = null;
        if (actEl.ValueKind == JsonValueKind.String)
            name = actEl.GetString();
        else if (actEl.ValueKind == JsonValueKind.Array && actEl.GetArrayLength() > 0)
        {
            var item = actEl[Math.Min(layerIdx, actEl.GetArrayLength() - 1)];
            if (item.ValueKind == JsonValueKind.String) name = item.GetString();
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var t)) name = t.GetString();
        }
        else if (actEl.ValueKind == JsonValueKind.Object && actEl.TryGetProperty("type", out var t2))
        {
            name = t2.GetString();
        }

        return (name ?? "Tanh").ToLowerInvariant() switch
        {
            "silu" or "swish" => WaveNetActKind.SiLU,
            "leakyrelu" or "leaky_relu" => WaveNetActKind.LeakyReLU,
            "relu" => WaveNetActKind.ReLU,
            "sigmoid" => WaveNetActKind.Sigmoid,
            _ => WaveNetActKind.Tanh,
        };
    }

    public static CompiledWaveNet? CompileWaveNet(WaveNetConfigDto cfg, float[] weights)
    {
        if (cfg.Layers.Count == 0 || weights.Length == 0 || cfg.Head != null)
            return null;

        try
        {
            int idx = 0;
            float Take() => weights[idx++];

            var arrays = new CompiledWaveNet.LayerArray[cfg.Layers.Count];
            int maxChPad = 8;
            int maxMidPad = 8;
            int maxHeadPad = 8;
            int maxDil = 1;

            for (int bi = 0; bi < cfg.Layers.Count; bi++)
            {
                var lc = cfg.Layers[bi];
                int inCh = Math.Max(1, lc.InputSize);
                int condCh = Math.Max(1, lc.ConditionSize);
                int ch = Math.Max(1, lc.Channels);
                int chPad = Pad8(ch);
                int bn = lc.Bottleneck is > 0 ? lc.Bottleneck.Value : ch;
                maxChPad = Math.Max(maxChPad, chPad);

                var l1Cfg = lc.Layer1x1 ?? lc.Layer1x1Alt;
                bool layer1x1Active = l1Cfg == null || l1Cfg.Active;
                var h1Cfg = lc.Head1x1 ?? lc.Head1x1Alt;
                bool head1x1Active = h1Cfg is { Active: true };
                int head1x1Out = head1x1Active ? Math.Max(1, h1Cfg!.OutChannels) : bn;

                int headOutCh = lc.Head != null ? Math.Max(1, lc.Head.OutChannels) : Math.Max(1, lc.HeadSize);
                int headK = lc.Head != null ? Math.Max(1, lc.Head.KernelSize) : 1;
                bool headBias = lc.Head != null ? lc.Head.Bias : lc.HeadBias;
                int headInCh = head1x1Active ? head1x1Out : bn;
                int headOutPad = Pad8(headOutCh);
                maxHeadPad = Math.Max(maxHeadPad, Math.Max(Pad8(headInCh), headOutPad));

                // 1) Rechannel[oc, ic] -> packed[ic, oc]
                var rechW = new float[inCh * chPad];
                for (int oc = 0; oc < ch; oc++)
                {
                    for (int ic = 0; ic < inCh; ic++)
                        rechW[ic * chPad + oc] = Take();
                }

                // 2) Layers
                int nLayers = lc.Dilations.Length;
                var layers = new CompiledWaveNet.Layer[nLayers];
                for (int li = 0; li < nLayers; li++)
                {
                    int kSize = lc.KernelSizes is { Length: > 0 }
                        ? lc.KernelSizes[Math.Min(li, lc.KernelSizes.Length - 1)]
                        : Math.Max(1, lc.KernelSize);
                    int dil = Math.Max(1, lc.Dilations[li]);
                    maxDil = Math.Max(maxDil, dil);

                    bool gated = lc.GatingMode is { Length: > 0 }
                        ? string.Equals(lc.GatingMode[Math.Min(li, lc.GatingMode.Length - 1)], "gated", StringComparison.OrdinalIgnoreCase)
                        : lc.Gated;

                    int midCh = gated ? 2 * bn : bn;
                    int midPad = Pad8(midCh);
                    maxMidPad = Math.Max(maxMidPad, midPad);

                    // Conv1d weight PyTorch layout: [midCh, ch, kSize] -> pack [k, ic, oc]
                    var convW = new float[kSize * ch * midPad];
                    for (int oc = 0; oc < midCh; oc++)
                    {
                        for (int ic = 0; ic < ch; ic++)
                        {
                            for (int k = 0; k < kSize; k++)
                                convW[(k * ch + ic) * midPad + oc] = Take();
                        }
                    }

                    var convB = new float[midPad];
                    for (int oc = 0; oc < midCh; oc++)
                        convB[oc] = Take();

                    // InputMixer weight [midCh, condCh] -> pack [c, oc]
                    var mixW = new float[condCh * midPad];
                    for (int oc = 0; oc < midCh; oc++)
                    {
                        for (int cc = 0; cc < condCh; cc++)
                            mixW[cc * midPad + oc] = Take();
                    }

                    float[] l1w = Array.Empty<float>();
                    float[] l1b = Array.Empty<float>();
                    if (layer1x1Active)
                    {
                        l1w = new float[bn * chPad];
                        for (int oc = 0; oc < ch; oc++)
                        {
                            for (int ic = 0; ic < bn; ic++)
                                l1w[ic * chPad + oc] = Take();
                        }
                        l1b = new float[chPad];
                        for (int oc = 0; oc < ch; oc++)
                            l1b[oc] = Take();
                    }

                    int padH1Out = Pad8(head1x1Out);
                    maxHeadPad = Math.Max(maxHeadPad, padH1Out);
                    float[] h1w = Array.Empty<float>();
                    if (head1x1Active)
                    {
                        h1w = new float[bn * padH1Out];
                        for (int oc = 0; oc < head1x1Out; oc++)
                        {
                            for (int ic = 0; ic < bn; ic++)
                                h1w[ic * padH1Out + oc] = Take();
                        }
                    }

                    int ringCap = NextPow2((kSize - 1) * dil + 4);
                    layers[li] = new CompiledWaveNet.Layer
                    {
                        Channels = ch,
                        PadChannels = chPad,
                        Bottleneck = bn,
                        MidChannels = midCh,
                        PadMidChannels = midPad,
                        ConditionSize = condCh,
                        KernelSize = kSize,
                        Dilation = dil,
                        RingCapacity = ringCap,
                        RingMask = ringCap - 1,
                        Gated = gated,
                        Act = ParseActivation(lc.Activation, li),
                        Layer1x1Active = layer1x1Active,
                        Head1x1Active = head1x1Active,
                        Head1x1Out = head1x1Out,
                        PadHeadOut = padH1Out,
                        ConvW = convW,
                        ConvB = convB,
                        InputMixerW = mixW,
                        Layer1x1W = l1w,
                        Layer1x1B = l1b,
                        Head1x1W = h1w,
                    };
                }

                // 3) HeadRechannel: [headOutCh, headInCh, headK] -> pack [k, ic, oc]
                var hRechW = new float[headK * headInCh * headOutPad];
                for (int oc = 0; oc < headOutCh; oc++)
                {
                    for (int ic = 0; ic < headInCh; ic++)
                    {
                        for (int k = 0; k < headK; k++)
                            hRechW[(k * headInCh + ic) * headOutPad + oc] = Take();
                    }
                }
                var hRechB = new float[headOutPad];
                if (headBias)
                {
                    for (int oc = 0; oc < headOutCh; oc++)
                        hRechB[oc] = Take();
                }

                int hRingCap = NextPow2(headK + 4);
                arrays[bi] = new CompiledWaveNet.LayerArray
                {
                    InputSize = inCh,
                    Channels = ch,
                    PadChannels = chPad,
                    HeadInChannels = headInCh,
                    HeadOutChannels = headOutCh,
                    PadHeadOutChannels = headOutPad,
                    HeadKernelSize = headK,
                    HeadRingCapacity = hRingCap,
                    HeadRingMask = hRingCap - 1,
                    RechannelW = rechW,
                    Layers = layers,
                    HeadRechannelW = hRechW,
                    HeadRechannelB = hRechB,
                };
            }

            float headScale = cfg.HeadScale;
            if (idx < weights.Length)
                headScale = Take();

            return new CompiledWaveNet
            {
                Arrays = arrays,
                HeadScale = headScale,
                MaxChannelsPadded = maxChPad,
                MaxMidChannelsPadded = maxMidPad,
                MaxHeadBusChannelsPadded = maxHeadPad,
                MaxDilation = maxDil,
            };
        }
        catch
        {
            return null;
        }
    }
}

public static class ToneProfileStore
{
    public static string ModelsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NeuralRigStudio", "models");

    public static string TonesRoot => ModelsRoot;
    public static string TonesDirectory => ModelsRoot;

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ToneProfile? Load(string jsonPath) => LoadFile(jsonPath);

    public static ToneProfile? LoadFile(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return null;
        var json = File.ReadAllText(jsonPath);
        var prof = JsonSerializer.Deserialize<ToneProfile>(json, ReadOpts);
        if (prof is null) return null;
        prof.JsonPath = jsonPath;
        prof.PrepareForRt();
        return prof;
    }

    public static List<ToneProfile> LoadAll()
    {
        var list = new List<ToneProfile>();
        if (!Directory.Exists(ModelsRoot))
            return list;

        foreach (var dir in Directory.GetDirectories(ModelsRoot))
        {
            string jsonPath = Path.Combine(dir, "tone_profile.json");
            if (!File.Exists(jsonPath)) continue;
            try
            {
                var p = LoadFile(jsonPath);
                if (p != null) list.Add(p);
            }
            catch
            {
                // 손상된 프로파일 무시
            }
        }
        return list.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 순수 C# 네이티브 .nam (Neural Amp Modeler v0.5+) 파서/임포터.
    /// Python 없이 즉시 WaveNet 가중치를 검증·컴파일하고 NeuralRigStudio 라이브러리로 저장한다.
    /// </summary>
    public static ToneProfile ImportNamFile(string namPath, string? customName = null)
    {
        if (!File.Exists(namPath))
            throw new FileNotFoundException(".nam 파일을 찾을 수 없습니다.", namPath);

        string json = File.ReadAllText(namPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string arch = root.TryGetProperty("architecture", out var archEl) ? (archEl.GetString() ?? "") : "";
        if (!string.Equals(arch, "WaveNet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"현재 WaveNet 아키텍처의 .nam 모델만 지원합니다. (감지된 형식: '{(string.IsNullOrEmpty(arch) ? "unknown" : arch)}')");

        if (!root.TryGetProperty("config", out var cfgEl))
            throw new InvalidOperationException(".nam 파일에 'config' 정의가 없습니다.");
        if (!root.TryGetProperty("weights", out var wEl) || wEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(".nam 파일에 'weights' 배열이 없습니다.");

        var cfg = JsonSerializer.Deserialize<WaveNetConfigDto>(cfgEl.GetRawText(), ReadOpts)
            ?? throw new InvalidOperationException("WaveNet config 역직렬화에 실패했습니다.");
        if (cfg.Head != null)
            throw new InvalidOperationException("현재 post-head 블록이 없는 표준 NAM WaveNet(.nam v0.5+)만 지원합니다.");

        var weights = new float[wEl.GetArrayLength()];
        int wi = 0;
        foreach (var item in wEl.EnumerateArray())
            weights[wi++] = item.GetSingle();

        int sampleRate = 44100;
        if (root.TryGetProperty("sample_rate", out var srEl) && srEl.TryGetInt32(out int parsedSr) && parsedSr > 8000)
            sampleRate = parsedSr;

        string? metaName = null;
        double esr = 0.0;
        if (root.TryGetProperty("metadata", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
        {
            if (metaEl.TryGetProperty("name", out var mn) && mn.ValueKind == JsonValueKind.String)
                metaName = mn.GetString();
            if (metaEl.TryGetProperty("esr", out var esrEl) && esrEl.ValueKind == JsonValueKind.Number && esrEl.TryGetDouble(out double parsedEsr))
                esr = Math.Max(0.0, parsedEsr);
        }

        if (ToneProfile.CompileWaveNet(cfg, weights) is null)
            throw new InvalidOperationException("WaveNet 가중치 구조 검증 또는 컴파일에 실패했습니다.");

        string displayName = !string.IsNullOrWhiteSpace(customName)
            ? customName.Trim()
            : (!string.IsNullOrWhiteSpace(metaName) ? metaName.Trim() : Path.GetFileNameWithoutExtension(namPath));
        string safeFile = string.Concat(displayName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safeFile)) safeFile = "ImportedModel";

        string archPreset = ClassifyWaveNetPreset(cfg);
        double esrDb = esr > 0.0 ? Math.Round(10.0 * Math.Log10(Math.Max(esr, 1e-12)), 2) : 0.0;
        double similarityPct = esr > 0.0 ? Math.Round(Math.Clamp((1.0 - Math.Sqrt(esr)) * 100.0, 0.0, 100.0), 2) : 0.0;

        string id = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        string folder = Path.Combine(ModelsRoot, id);
        Directory.CreateDirectory(folder);

        string destNam = Path.Combine(folder, $"{safeFile}.nam");
        File.Copy(namPath, destNam, overwrite: true);

        var prof = new ToneProfile
        {
            Id = id,
            Name = displayName,
            Version = 3,
            Architecture = "WaveNet",
            CaptureKind = "nam_import",
            ArchPreset = archPreset,
            DefaultToneMode = (int)ToneMode.FullRig,
            SampleRate = sampleRate,
            ToneClass = "NAM Model",
            GpuDevice = "Native C# Import",
            Loss = esr,
            Esr = esr,
            EsrDb = esrDb,
            SimilarityPct = similarityPct,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            SourceUrl = namPath,
            WaveNetConfig = cfg,
            Weights = weights,
            NamFilePath = destNam,
        };

        string jsonPath = Path.Combine(folder, "tone_profile.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(prof, WriteOpts));
        prof.JsonPath = jsonPath;
        prof.PrepareForRt();
        return prof;
    }

    private static string ClassifyWaveNetPreset(WaveNetConfigDto cfg)
    {
        if (cfg.Layers.Count == 1 && cfg.Layers[0].Channels <= 12) return "nano";
        if (cfg.Layers.Count == 2 && cfg.Layers[0].Channels <= 12) return "feather";
        if (cfg.Layers.Count >= 2 && cfg.Layers[0].Channels >= 16) return "standard";
        return "custom";
    }

    public static void Delete(ToneProfile prof)
    {
        if (prof is null) return;
        if (!string.IsNullOrEmpty(prof.JsonPath))
        {
            string? dir = Path.GetDirectoryName(prof.JsonPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
                return;
            }
        }
        Delete(prof.Id);
    }

    public static void Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        string dir = Path.Combine(ModelsRoot, id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    public static void ExportNamFile(ToneProfile prof, string destinationPath)
    {
        if (!string.IsNullOrEmpty(prof.NamFilePath) && File.Exists(prof.NamFilePath))
        {
            File.Copy(prof.NamFilePath, destinationPath, overwrite: true);
            return;
        }
        if (prof.WaveNetConfig is null || prof.Weights.Length == 0)
            throw new InvalidOperationException("WaveNet 가중치가 없는 프로파일은 .nam으로 내보낼 수 없습니다.");

        var namObj = new
        {
            version = "0.5.4",
            metadata = new
            {
                name = prof.Name,
                modeled_by = "NeuralRig Studio",
                esr = prof.Esr > 0 ? prof.Esr : (double?)null,
            },
            architecture = "WaveNet",
            sample_rate = prof.SampleRate > 0 ? prof.SampleRate : 44100,
            config = prof.WaveNetConfig,
            weights = prof.Weights,
        };
        File.WriteAllText(destinationPath, JsonSerializer.Serialize(namObj, WriteOpts));
    }
}

