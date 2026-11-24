using System.Text.Json;

namespace NeuralRigStudio.Audio;

public sealed class MidiBindingDto
{
    public string TargetKind { get; set; } = "param"; // param | bypass | scene | tapTempo | tunerMute
    public int Cc { get; set; }
    public int Channel { get; set; }                  // 0 = Omni, 1..16 = specific channel
    public int SlotIndex { get; set; }
    public int ParamIndex { get; set; }
    public int SceneIndex { get; set; }

    public MidiBindingDto Clone() => new()
    {
        TargetKind = TargetKind ?? "param",
        Cc = Math.Clamp(Cc, 0, 127),
        Channel = Math.Clamp(Channel, 0, 16),
        SlotIndex = Math.Clamp(SlotIndex, 0, 15),
        ParamIndex = Math.Clamp(ParamIndex, 0, 7),
        SceneIndex = Math.Clamp(SceneIndex, 0, 7),
    };
}

public sealed class AppSettings
{
    public string InputKind { get; set; } = "ASIO";  // "ASIO" | "WASAPI"
    public string? InputDriver { get; set; }
    public string? WasapiInputDeviceId { get; set; } // null/"" = 시스템 기본 녹음 장치
    public bool WasapiInputExclusive { get; set; }
    public bool WasapiInputRaw { get; set; } = true; // Windows APO 노이즈 억제/AGC 우회 (순수 기타 입력 보존)
    public int ChannelMode { get; set; } = 1;        // 0: 스테레오(Offset, Offset+1), 1: 모노(Offset), 2: 모노 Ch2(하위호환)
    public int InputChannelOffset { get; set; } = 0; // 다채널 인터페이스용 0-based 시작 입력 채널
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
    public string? MidiInputName { get; set; }
    public List<MidiBindingDto> MidiBindings { get; set; } = new();

    // Onboarding & Public Cloud Sync (v3.0)
    public bool HasChosenAuthMode { get; set; }
    public bool HasCompletedTutorial { get; set; }
    public string? CloudApiBaseUrl { get; set; }
    public string? LastCloudSyncUtc { get; set; }
    public bool AutoCloudSync { get; set; } = true;
    public string? LastGuestLoginPromptDate { get; set; }
    public string? FirstGoogleSignInUtc { get; set; }
    public bool HasShownSponsorPrompt { get; set; }

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
