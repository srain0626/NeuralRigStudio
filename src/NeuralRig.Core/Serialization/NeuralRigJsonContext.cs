using System.Text.Json;
using System.Text.Json.Serialization;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Services;

namespace NeuralRigStudio.Serialization;

public sealed class NamMetadataExportDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("modeled_by")]
    public string ModeledBy { get; set; } = "NeuralRig Studio";

    [JsonPropertyName("esr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Esr { get; set; }
}

public sealed class NamFileExportDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.5.4";

    [JsonPropertyName("metadata")]
    public NamMetadataExportDto Metadata { get; set; } = new();

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = "WaveNet";

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; } = 44100;

    [JsonPropertyName("config")]
    public JsonElement Config { get; set; }

    [JsonPropertyName("weights")]
    public float[] Weights { get; set; } = Array.Empty<float>();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(WhiteBoxParams))]
[JsonSerializable(typeof(SignalChainConfigDto))]
[JsonSerializable(typeof(List<SignalChainConfigDto>))]
[JsonSerializable(typeof(ChainSlotDto))]
[JsonSerializable(typeof(CabMicConfigDto))]
[JsonSerializable(typeof(SceneSnapshotDto))]
[JsonSerializable(typeof(SceneSlotStateDto))]
[JsonSerializable(typeof(MidiBindingDto))]
[JsonSerializable(typeof(GuitarProfileDto))]
[JsonSerializable(typeof(List<GuitarProfileDto>))]
[JsonSerializable(typeof(GuitarNoteMeasurementDto))]
[JsonSerializable(typeof(GuitarAnalysisLiveStateDto))]
[JsonSerializable(typeof(ToneProfile))]
[JsonSerializable(typeof(WaveNetConfigDto))]
[JsonSerializable(typeof(WaveNetLayerArrayCfg))]
[JsonSerializable(typeof(WaveNetHeadCfg))]
[JsonSerializable(typeof(LayerArrayHeadCfg))]
[JsonSerializable(typeof(Layer1x1Cfg))]
[JsonSerializable(typeof(Head1x1Cfg))]
[JsonSerializable(typeof(LstmConfigDto))]
[JsonSerializable(typeof(ConvNetConfigDto))]
[JsonSerializable(typeof(NamFileExportDto))]
[JsonSerializable(typeof(NamMetadataExportDto))]
[JsonSerializable(typeof(CustomDeviceSpecDto))]
[JsonSerializable(typeof(List<CustomDeviceSpecDto>))]
[JsonSerializable(typeof(CustomDeviceSpecDto[]))]
[JsonSerializable(typeof(SpiceCircuitDto))]
[JsonSerializable(typeof(SpiceStageDto))]
[JsonSerializable(typeof(SpiceElementDto))]
[JsonSerializable(typeof(SpiceKnobDefDto))]
[JsonSerializable(typeof(SpiceFixedVoltageDto))]
[JsonSerializable(typeof(SpiceComponentModelDto))]
[JsonSerializable(typeof(List<SpiceComponentModelDto>))]
[JsonSerializable(typeof(CustomCabAcousticDto))]
[JsonSerializable(typeof(FreqResponsePointDto))]
[JsonSerializable(typeof(NrigManifestDto))]
[JsonSerializable(typeof(NrigNamEntryDto))]
[JsonSerializable(typeof(NrigCabIrEntryDto))]
[JsonSerializable(typeof(ToneHubItemDto))]
[JsonSerializable(typeof(ToneHubItemDto[]))]
[JsonSerializable(typeof(LooperSnapshotDto))]
[JsonSerializable(typeof(RecorderReampSnapshotDto))]
[JsonSerializable(typeof(RigBenchmarkReportDto))]
[JsonSerializable(typeof(HardwareSimilarityItemDto))]
[JsonSerializable(typeof(LatencyBenchItemDto))]
[JsonSerializable(typeof(MultiChainStressItemDto))]
[JsonSerializable(typeof(CloudUserDto))]
[JsonSerializable(typeof(CloudStateDto))]
[JsonSerializable(typeof(CloudAuthSession))]
[JsonSerializable(typeof(CommunityGearItemDto))]
[JsonSerializable(typeof(CommunityGearItemDto[]))]
[JsonSerializable(typeof(QueuedBugReportDto))]
[JsonSerializable(typeof(List<QueuedBugReportDto>))]
[JsonSerializable(typeof(OtaCheckResultDto))]
[JsonSerializable(typeof(string))]
public sealed partial class NeuralRigJsonContext : JsonSerializerContext
{
    private static readonly JsonElement CachedTanhElement = ParseLiteralJson("\"Tanh\"");

    public static JsonElement DefaultTanhElement => CachedTanhElement.Clone();

    private static JsonElement ParseLiteralJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ToneProfile))]
[JsonSerializable(typeof(NamFileExportDto))]
[JsonSerializable(typeof(NrigManifestDto))]
[JsonSerializable(typeof(List<CustomDeviceSpecDto>))]
[JsonSerializable(typeof(List<SpiceComponentModelDto>))]
[JsonSerializable(typeof(List<QueuedBugReportDto>))]
public sealed partial class NeuralRigIndentedJsonContext : JsonSerializerContext
{
}
