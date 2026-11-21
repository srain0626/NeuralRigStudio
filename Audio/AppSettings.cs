using System.Text.Json;

namespace NeuralRigStudio.Audio;

public sealed class AppSettings
{
    public string? InputDriver { get; set; }
    public int ChannelMode { get; set; } = 1;        // 1: 모노 In 1 (기타 입력 좌→스테레오 양쪽)
    public int AsioInBufferSize { get; set; }
    public string OutputKind { get; set; } = "WASAPI";
    public bool WasapiExclusive { get; set; }
    public string? WasapiDeviceId { get; set; }
    public string? AsioOutDriver { get; set; }
    public int PresetIndex { get; set; } = 1;        // 1: 최소 (링 8ms / WASAPI 10~15ms)

    public string? SelectedToneId { get; set; }
    public int ToneMode { get; set; } = 6;           // 기본 SignalChain (6): Quad Cortex 모듈러 시그널 체인
    public float ToneDrivePct { get; set; } = 100f;  // 20..220
    public float ToneMixPct { get; set; } = 100f;    // 0..100
    public float ToneGainDb { get; set; } = 0f;      // -12..+12
    public float ToneGateDb { get; set; } = -80f;    // -80..-28 (default off)
    public string? CustomCabIrPath { get; set; }
    public WhiteBoxParams WhiteBox { get; set; } = new();
    public SignalChainConfigDto SignalChain { get; set; } = new();
    public List<SignalChainConfigDto> SavedPresets { get; set; } = new();
    public List<GuitarProfileDto> GuitarProfiles { get; set; } = new();
    public string? ActiveGuitarProfileId { get; set; }

    private static string FilePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NeuralRigStudio");
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // 손상된 설정은 무시하고 기본값 사용
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 설정 저장 실패는 치명적이지 않음
        }
    }
}
