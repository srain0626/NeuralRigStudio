using System.Text.Json;
using System.Text.Json.Serialization;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Services;

namespace NeuralRigStudio.Plugin;

public sealed class PluginStateEnvelope
{
    public int FormatVersion { get; set; } = 3;
    public string? SelectedToneId { get; set; }
    public int ToneMode { get; set; } = (int)Audio.ToneMode.SignalChain;
    public string? CustomCabIrPath { get; set; }
    public float DriveMultiplier { get; set; } = 1.0f;
    public float WetMix { get; set; } = 1.0f;
    public float OutputGainDb { get; set; }
    public float GateThresholdDb { get; set; } = -56f;
    public bool HostBpmSync { get; set; } = true;
    public bool TunerMute { get; set; }
    public WhiteBoxParams? WhiteBox { get; set; }
    public SignalChainConfigDto? SignalChain { get; set; }
    public float[] ParamsNorm { get; set; } = Array.Empty<float>();
    public List<SignalChainConfigDto>? SavedPresets { get; set; }
    public List<GuitarProfileDto>? GuitarProfiles { get; set; }
    public string? ActiveGuitarProfileId { get; set; }
}

public sealed class Vst3FactoryFlagsDto
{
    [JsonPropertyName("Unicode")]
    public bool Unicode { get; set; } = true;

    [JsonPropertyName("Classes Discardable")]
    public bool ClassesDiscardable { get; set; }

    [JsonPropertyName("Component Non Discardable")]
    public bool ComponentNonDiscardable { get; set; }
}

public sealed class Vst3FactoryInfoDto
{
    [JsonPropertyName("Vendor")]
    public string Vendor { get; set; } = "NeuralRig Audio";

    [JsonPropertyName("URL")]
    public string Url { get; set; } = "https://neuralrig.studio";

    [JsonPropertyName("E-Mail")]
    public string Email { get; set; } = "support@neuralrig.studio";

    [JsonPropertyName("Flags")]
    public Vst3FactoryFlagsDto Flags { get; set; } = new();
}

public sealed class Vst3ClassInfoDto
{
    [JsonPropertyName("CID")]
    public string Cid { get; set; } = "";

    [JsonPropertyName("Category")]
    public string Category { get; set; } = "Audio Module Class";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "NeuralRig Studio";

    [JsonPropertyName("Vendor")]
    public string Vendor { get; set; } = "NeuralRig Audio";

    [JsonPropertyName("Version")]
    public string Version { get; set; } = "3.0.0";

    [JsonPropertyName("SDKVersion")]
    public string SdkVersion { get; set; } = "VST 3.7.11";

    [JsonPropertyName("Sub Categories")]
    public string[] SubCategories { get; set; } = new[] { "Fx" };

    [JsonPropertyName("Class Flags")]
    public int ClassFlags { get; set; } = 1;

    [JsonPropertyName("Cardinality")]
    public long Cardinality { get; set; } = 2147483647;

    [JsonPropertyName("Snapshots")]
    public string[] Snapshots { get; set; } = Array.Empty<string>();
}

public sealed class Vst3ModuleInfoDto
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "NeuralRig Studio";

    [JsonPropertyName("Version")]
    public string Version { get; set; } = "3.0.0";

    [JsonPropertyName("Factory Info")]
    public Vst3FactoryInfoDto FactoryInfo { get; set; } = new();

    [JsonPropertyName("Classes")]
    public List<Vst3ClassInfoDto> Classes { get; set; } = new();
}

public sealed class ClapManifestDto
{
    public string Schema { get; set; } = "clap-v1.2";
    public string PluginId { get; set; } = "";
    public string PluginName { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] Features { get; set; } = Array.Empty<string>();
    public string DefaultRigFile { get; set; } = "";
    public int ParameterCount { get; set; }
}

public sealed record PluginPresetDefDto(int Index, string Name, int RingMs, int SharedMs, int ExclMs);

public sealed record PluginModelProfileDto(
    string Id,
    string Name,
    string Architecture,
    string ArchPreset,
    int SampleRate,
    double Esr,
    double EsrDb,
    double SimilarityPct,
    string CreatedAt,
    string NamFilePath,
    bool HasCabIr);

public sealed class PluginAudioDeviceOptionDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class PluginAudioDevicesSnapshotDto
{
    public string[] AsioDrivers { get; set; } = Array.Empty<string>();
    public PluginAudioDeviceOptionDto[] WasapiDevices { get; set; } = Array.Empty<PluginAudioDeviceOptionDto>();
    public PluginAudioDeviceOptionDto[] WasapiInputDevices { get; set; } = Array.Empty<PluginAudioDeviceOptionDto>();
    public string? InitialDriver { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InitialCaps { get; set; }
}

public sealed record PluginBackingStateDto(
    bool Loaded,
    bool Playing,
    bool Loop,
    float Volume,
    double PositionSec,
    double DurationSec,
    string FileName,
    string FilePath);

public sealed record PluginMidiLearnTargetDto(string TargetKind, int SlotIndex, int ParamIndex, int SceneIndex);

public sealed class PluginMidiSnapshotDto
{
    public string[] Devices { get; set; } = Array.Empty<string>();
    public string ActiveDevice { get; set; } = "";
    public bool Connected { get; set; }
    public List<MidiBindingDto> Bindings { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginMidiLearnTargetDto? Learning { get; set; }
}

public sealed class PluginDawHostInfoDto
{
    public bool IsPluginHost { get; set; } = true;
    public string Format { get; set; } = "VST3";
    public int SampleRate { get; set; } = 48000;
    public int MaxBlockSize { get; set; } = 512;
    public int ParameterCount { get; set; } = DawParameterCatalog.TotalParameterCount;
    public double HostBpm { get; set; } = 120.0;
    public bool HostBpmSync { get; set; } = true;
}

public sealed class PluginLatencyDto
{
    public double InMs { get; set; }
    public double RingMs { get; set; }
    public double OutMs { get; set; }
    public double TotalMs { get; set; }
}

public sealed class PluginCountersDto
{
    public long Padded { get; set; }
    public long Dropped { get; set; }
    public long Lost { get; set; }
    public int SampleRate { get; set; }
}

public sealed class PluginVisDto
{
    public bool Running { get; set; }
    public bool HasSignal { get; set; }
    public bool HasIrCurve { get; set; }
    public float PeakDb { get; set; }
    public float[] Bands { get; set; } = Array.Empty<float>();
    public float[] PeakCaps { get; set; } = Array.Empty<float>();
    public float[] Wave { get; set; } = Array.Empty<float>();
    public float[] IrCurve { get; set; } = Array.Empty<float>();
    public TunerSnapshot? Tuner { get; set; }
}

public sealed class PluginTelemetryDataDto
{
    public bool Running { get; set; }
    public bool Busy { get; set; }
    public bool DirectDuplex { get; set; }
    public float MeterL { get; set; }
    public float MeterR { get; set; }
    public bool Clip { get; set; }
    public float InMeterL { get; set; }
    public float InMeterR { get; set; }
    public bool InClip { get; set; }
    public GuitarAnalysisLiveStateDto? GuitarAnalysis { get; set; }
    public PluginLatencyDto? Latency { get; set; }
    public PluginCountersDto Counters { get; set; } = new();
    public PluginBackingStateDto Backing { get; set; } = new(false, false, true, 0.8f, 0, 0, "", "");
    public LooperSnapshotDto? Looper { get; set; }
    public RecorderReampSnapshotDto? Recorder { get; set; }
    public PluginVisDto Vis { get; set; } = new();
}

public sealed class PluginInitSnapshotDto
{
    public string HostMode { get; set; } = "vst3";
    public PluginDawHostInfoDto DawHost { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public PluginPresetDefDto[] Presets { get; set; } = Array.Empty<PluginPresetDefDto>();
    public PluginAudioDevicesSnapshotDto Audio { get; set; } = new();
    public List<PluginModelProfileDto> Tones { get; set; } = new();
    public string SelectedToneId { get; set; } = "";
    public string CustomCabIrName { get; set; } = "";
    public WhiteBoxParams WhiteBox { get; set; } = new();
    public SignalChainConfigDto SignalChain { get; set; } = new();
    public bool IoGainLock { get; set; }
    public List<SignalChainConfigDto> SavedPresets { get; set; } = new();
    public List<GuitarProfileDto> GuitarProfiles { get; set; } = new();
    public string ActiveGuitarProfileId { get; set; } = "";
    public bool TunerMute { get; set; }
    public float TunerRefA4Hz { get; set; } = 440f;
    public PluginBackingStateDto Backing { get; set; } = new(false, false, true, 0.8f, 0, 0, "", "");
    public LooperSnapshotDto? Looper { get; set; }
    public RecorderReampSnapshotDto? Recorder { get; set; }
    public PluginMidiSnapshotDto Midi { get; set; } = new();
    public CustomDeviceSpecDto[] CustomDevices { get; set; } = Array.Empty<CustomDeviceSpecDto>();
    public bool IsAudioRunning { get; set; }
    public string AppVersion { get; set; } = "3.0.0";
    public CloudStateDto CloudState { get; set; } = new();
}

public sealed class PluginRpcResponseDto
{
    public string Type { get; set; } = "response";
    public string ReqId { get; set; } = "";
    public bool Ok { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

public sealed class PluginRpcEventDto
{
    public string Type { get; set; } = "";
    public JsonElement Data { get; set; }
}

public sealed class PluginOkResDto
{
    public bool Ok { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ToneMode { get; set; }
}

public sealed class PluginUpdateChainResDto
{
    public bool Ok { get; set; } = true;
    public SignalChainConfigDto SignalChain { get; set; } = new();
}

public sealed class PluginIoGainResDto
{
    public bool Ok { get; set; } = true;
    public float InputGainDb { get; set; }
    public float OutputGainDb { get; set; }
    public bool IoGainLock { get; set; }
}

public sealed class PluginWhiteBoxResDto
{
    public bool Ok { get; set; } = true;
    public WhiteBoxParams WhiteBox { get; set; } = new();
}

public sealed class PluginSavedPresetsResDto
{
    public bool Ok { get; set; } = true;
    public List<SignalChainConfigDto> SavedPresets { get; set; } = new();
}

public sealed class PluginSelectToneResDto
{
    public string SelectedToneId { get; set; } = "";
    public string CustomCabIrName { get; set; } = "";
    public int ToneMode { get; set; }
}

public sealed class PluginDeleteToneResDto
{
    public bool Deleted { get; set; }
    public List<PluginModelProfileDto> Tones { get; set; } = new();
    public string SelectedToneId { get; set; } = "";
    public int ToneMode { get; set; }
}

public sealed class PluginTonesChangedDto
{
    public List<PluginModelProfileDto> Tones { get; set; } = new();
    public string SelectedToneId { get; set; } = "";
    public int ToneMode { get; set; }
}

public sealed class PluginSignalChainChangedDto
{
    public SignalChainConfigDto SignalChain { get; set; } = new();
    public List<SignalChainConfigDto> SavedPresets { get; set; } = new();
    public string Summary { get; set; } = "";
}

public sealed class PluginImportNamResDto
{
    public bool Cancelled { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginModelProfileDto? Profile { get; set; }
    public List<PluginModelProfileDto> Tones { get; set; } = new();
    public string SelectedToneId { get; set; } = "";
    public int ToneMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SignalChainConfigDto? SignalChain { get; set; }
}

public sealed class PluginExportPathResDto
{
    public bool Cancelled { get; set; }
    public string Path { get; set; } = "";
}

public sealed class PluginCabIrResDto
{
    public bool Cancelled { get; set; }
    public bool Loaded { get; set; }
    public string CustomCabIrName { get; set; } = "";
    public int Taps { get; set; }
}

public sealed class PluginCabIrChangedDto
{
    public string CustomCabIrName { get; set; } = "";
}

public sealed class PluginTunerConfigResDto
{
    public bool Mute { get; set; }
    public float RefA4Hz { get; set; } = 440f;
}

public sealed class PluginGuitarProfilesResDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GuitarProfileDto? Profile { get; set; }
    public List<GuitarProfileDto> GuitarProfiles { get; set; } = new();
    public string ActiveGuitarProfileId { get; set; } = "";
}

public sealed class PluginApplyGuitarCalResDto
{
    public string ProfileName { get; set; } = "";
    public float InputGainDb { get; set; }
    public float GateDb { get; set; }
    public SignalChainConfigDto SignalChain { get; set; } = new();
}

public sealed class PluginAiToneResDto
{
    public bool Ok { get; set; } = true;
    public string PresetName { get; set; } = "";
    public string Summary { get; set; } = "";
    public string[] Highlights { get; set; } = Array.Empty<string>();
    public SignalChainConfigDto SignalChain { get; set; } = new();
    public List<SignalChainConfigDto> SavedPresets { get; set; } = new();
}

public sealed class PluginExportNrigResDto
{
    public bool Cancelled { get; set; }
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int NamCount { get; set; }
    public bool HasCabIr { get; set; }
}

public sealed class PluginImportNrigResDto
{
    public bool Cancelled { get; set; }
    public SignalChainConfigDto SignalChain { get; set; } = new();
    public List<SignalChainConfigDto> SavedPresets { get; set; } = new();
    public List<PluginModelProfileDto> Tones { get; set; } = new();
    public string SelectedToneId { get; set; } = "";
    public string CustomCabIrName { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class PluginLogEventDto
{
    public string Line { get; set; } = "";
}

public sealed class PluginCustomDevicesChangedDto
{
    public CustomDeviceSpecDto[] Devices { get; set; } = Array.Empty<CustomDeviceSpecDto>();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }
}

public sealed class PluginDawParamSyncEventDto
{
    public SignalChainConfigDto SignalChain { get; set; } = new();
    public double HostBpm { get; set; } = 120.0;
    public bool HostBpmSync { get; set; } = true;
    public float InputGainDb { get; set; }
    public float OutputGainDb { get; set; }
    public float GateThresholdDb { get; set; } = -56f;
    public float DriveMultiplier { get; set; } = 1.0f;
    public float WetMix { get; set; } = 100f;
}

public sealed class PluginSanitizedLogsDto
{
    public string Logs { get; set; } = "";
    public int PendingCount { get; set; }
}

public sealed class PluginToneHubInstallResDto
{
    public string Summary { get; set; } = "";
    public ToneHubItemDto[] Catalog { get; set; } = Array.Empty<ToneHubItemDto>();
}

public sealed class DawPluginParamSummaryDto
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Module { get; set; } = "";
    public string Unit { get; set; } = "";
    public float Min { get; set; }
    public float Max { get; set; }
    public float DefaultVal { get; set; }
    public int Steps { get; set; }
}

public sealed class DawPluginInspectorDto
{
    public string PluginName { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Version { get; set; } = "";
    public string ClapId { get; set; } = "";
    public string Vst3ProcessorCid { get; set; } = "";
    public string Vst3ControllerCid { get; set; } = "";
    public string SubCategories { get; set; } = "";
    public string NativeAotCommand { get; set; } = "";
    public string DefaultOutputDir { get; set; } = "";
    public DawPluginParamSummaryDto[] Parameters { get; set; } = Array.Empty<DawPluginParamSummaryDto>();
}

public sealed class PluginToneHubCatalogResDto
{
    public ToneHubItemDto[] Items { get; set; } = Array.Empty<ToneHubItemDto>();
    public DawPluginInspectorDto DawPlugin { get; set; } = new();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(PluginStateEnvelope))]
[JsonSerializable(typeof(ClapManifestDto))]
[JsonSerializable(typeof(PluginPresetDefDto))]
[JsonSerializable(typeof(PluginPresetDefDto[]))]
[JsonSerializable(typeof(PluginModelProfileDto))]
[JsonSerializable(typeof(List<PluginModelProfileDto>))]
[JsonSerializable(typeof(PluginAudioDevicesSnapshotDto))]
[JsonSerializable(typeof(PluginAudioDeviceOptionDto))]
[JsonSerializable(typeof(PluginBackingStateDto))]
[JsonSerializable(typeof(PluginMidiLearnTargetDto))]
[JsonSerializable(typeof(PluginMidiSnapshotDto))]
[JsonSerializable(typeof(DawPluginParamSummaryDto))]
[JsonSerializable(typeof(DawPluginInspectorDto))]
[JsonSerializable(typeof(PluginToneHubCatalogResDto))]
[JsonSerializable(typeof(PluginDawHostInfoDto))]
[JsonSerializable(typeof(PluginLatencyDto))]
[JsonSerializable(typeof(PluginCountersDto))]
[JsonSerializable(typeof(PluginVisDto))]
[JsonSerializable(typeof(PluginTelemetryDataDto))]
[JsonSerializable(typeof(PluginInitSnapshotDto))]
[JsonSerializable(typeof(PluginRpcResponseDto))]
[JsonSerializable(typeof(PluginRpcEventDto))]
[JsonSerializable(typeof(PluginOkResDto))]
[JsonSerializable(typeof(PluginUpdateChainResDto))]
[JsonSerializable(typeof(PluginIoGainResDto))]
[JsonSerializable(typeof(PluginWhiteBoxResDto))]
[JsonSerializable(typeof(PluginSavedPresetsResDto))]
[JsonSerializable(typeof(PluginSelectToneResDto))]
[JsonSerializable(typeof(PluginDeleteToneResDto))]
[JsonSerializable(typeof(PluginTonesChangedDto))]
[JsonSerializable(typeof(PluginSignalChainChangedDto))]
[JsonSerializable(typeof(PluginImportNamResDto))]
[JsonSerializable(typeof(PluginExportPathResDto))]
[JsonSerializable(typeof(PluginCabIrResDto))]
[JsonSerializable(typeof(PluginCabIrChangedDto))]
[JsonSerializable(typeof(PluginTunerConfigResDto))]
[JsonSerializable(typeof(PluginGuitarProfilesResDto))]
[JsonSerializable(typeof(PluginApplyGuitarCalResDto))]
[JsonSerializable(typeof(PluginAiToneResDto))]
[JsonSerializable(typeof(PluginExportNrigResDto))]
[JsonSerializable(typeof(PluginImportNrigResDto))]
[JsonSerializable(typeof(PluginLogEventDto))]
[JsonSerializable(typeof(PluginCustomDevicesChangedDto))]
[JsonSerializable(typeof(PluginDawParamSyncEventDto))]
[JsonSerializable(typeof(PluginSanitizedLogsDto))]
[JsonSerializable(typeof(PluginToneHubInstallResDto))]
[JsonSerializable(typeof(ToneHubItemDto[]))]
[JsonSerializable(typeof(LooperSnapshotDto))]
[JsonSerializable(typeof(RecorderReampSnapshotDto))]
[JsonSerializable(typeof(RigBenchmarkReportDto))]
[JsonSerializable(typeof(GuitarAnalysisLiveStateDto))]
[JsonSerializable(typeof(GuitarProfileDto))]
[JsonSerializable(typeof(SignalChainConfigDto))]
[JsonSerializable(typeof(WhiteBoxParams))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CustomDeviceSpecDto[]))]
[JsonSerializable(typeof(CloudStateDto))]
[JsonSerializable(typeof(TunerSnapshot))]
public sealed partial class PluginJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(PluginStateEnvelope))]
[JsonSerializable(typeof(ClapManifestDto))]
[JsonSerializable(typeof(SignalChainConfigDto))]
public sealed partial class PluginIndentedJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(Vst3ModuleInfoDto))]
public sealed partial class Vst3ModuleInfoJsonContext : JsonSerializerContext
{
}
