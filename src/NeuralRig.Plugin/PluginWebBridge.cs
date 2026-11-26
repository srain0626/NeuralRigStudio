using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Services;

namespace NeuralRigStudio.Plugin;

/// <summary>
/// WinForms 의존성이 없는 순수 NativeAOT 호환 VST3/CLAP WebView2 IPC 브리지.
/// </summary>
public sealed class PluginWebBridge : IDisposable
{
    public static readonly PluginPresetDefDto[] Presets =
    {
        new(0, "극한 (링 4ms / WASAPI 8~12ms)", 4, 12, 8),
        new(1, "최소 (링 8ms / WASAPI 10~15ms)", 8, 15, 10),
        new(2, "균형 (링 16ms / WASAPI 15~25ms)", 16, 25, 15),
        new(3, "안정 (링 32ms / WASAPI 25~50ms)", 32, 50, 25),
    };

    private readonly NeuralRigPluginKernel _kernel;
    private readonly Action<string> _sendJson;
    private readonly Action<uint, int, double>? _onHostParamGesture;
    private readonly VisualizerAnalyzer _visualizer = new();
    private readonly GuitarAnalyzer _guitarAnalyzer = new();
    private readonly AntigravityToneArchitect _toneArchitect = new();
    private readonly CloudPlatformService _cloud = new();
    private readonly float[] _guitarChunk = new float[4096];
    private readonly CancellationTokenSource _cts = new();

    private AppSettings _settings;
    private List<ToneProfile> _models = new();
    private nint _parentHwnd;
    private bool _wasIdleSettled;
    private float _meterL;
    private float _meterR;
    private float _inMeterL;
    private float _inMeterR;
    private long _inClipHoldUntil;
    private long _outClipHoldUntil;
    private int _guitarFeedSkip;

    public PluginWebBridge(
        NeuralRigPluginKernel kernel,
        Action<string> sendJson,
        Action<uint, int, double>? onHostParamGesture = null)
    {
        _kernel = kernel;
        _sendJson = sendJson;
        _onHostParamGesture = onHostParamGesture;
        _settings = AppSettings.Load();
        LoadInitialState();
    }

    public void SetParentHwnd(nint hwnd) => _parentHwnd = hwnd;

    private void LoadInitialState()
    {
        _models = ToneProfileStore.LoadAll();
        _kernel.Processor.SetModelLibrary(_models);

        ToneProfile? selected = null;
        if (!string.IsNullOrEmpty(_settings.SelectedToneId))
            selected = _models.FirstOrDefault(t => t.Id == _settings.SelectedToneId);
        selected ??= _models.FirstOrDefault();
        _kernel.Processor.ActiveProfile = selected;

        if (!string.IsNullOrEmpty(_settings.CustomCabIrPath) && File.Exists(_settings.CustomCabIrPath))
        {
            try { _kernel.Processor.LoadCustomCabIr(_settings.CustomCabIrPath); }
            catch { _settings.CustomCabIrPath = null; }
        }

        _settings.WhiteBox ??= new WhiteBoxParams();
        _kernel.Processor.WhiteBox.UpdateParams(_settings.WhiteBox);
        _settings.SignalChain ??= new SignalChainConfigDto();
        _settings.SavedPresets ??= new List<SignalChainConfigDto>();
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        _settings.MidiBindings ??= new List<MidiBindingDto>();

        SpiceModelLibrary.LoadAll();
        foreach (var cSpec in CustomDeviceRegistry.GetAll())
            AntigravityToneArchitect.RegisterCustomDevice(cSpec);

        _kernel.LoadChainConfig(_settings.SignalChain);

        int initMode = Math.Clamp(_settings.ToneMode, 0, 6);
        if (selected is null && (initMode == (int)ToneMode.FullRig || initMode == (int)ToneMode.AmpHeadOnly))
            initMode = (int)ToneMode.SignalChain;
        _kernel.Processor.Mode = (ToneMode)initMode;
        _kernel.Processor.DriveMultiplier = Math.Clamp(_settings.ToneDrivePct, 20f, 220f) / 100f;
        _kernel.Processor.WetMix = Math.Clamp(_settings.ToneMixPct, 0f, 100f) / 100f;
        _kernel.Processor.OutputGainDb = Math.Clamp(_settings.ToneGainDb, -12f, 12f);
        _kernel.Processor.GateThresholdDb = Math.Clamp(_settings.ToneGateDb, -80f, -20f);
        _kernel.SyncParamsFromEngine();
        SyncVisualizerCabIr();
    }

    private void SyncVisualizerCabIr()
    {
        var prof = _kernel.Processor.ActiveProfile;
        int sr = _kernel.EffectiveSampleRate > 0 ? _kernel.EffectiveSampleRate : (prof?.SampleRate ?? 44100);
        _visualizer.SetCabIr(_kernel.Processor.GetEffectiveCabIr(), sr);
    }

    public byte[] SavePluginStateBytes()
        => _kernel.SaveStateBytes(
            _settings.SavedPresets,
            _settings.GuitarProfiles,
            _settings.ActiveGuitarProfileId);

    public bool LoadPluginStateBytes(ReadOnlySpan<byte> bytes)
    {
        if (!_kernel.TryLoadStateEnvelope(bytes, out var env) || env == null)
            return false;

        _models = ToneProfileStore.LoadAll();
        _settings.ToneMode = env.ToneMode;
        _settings.SelectedToneId = env.SelectedToneId;
        _settings.CustomCabIrPath = env.CustomCabIrPath;
        _settings.ToneDrivePct = Math.Clamp(env.DriveMultiplier * 100f, 20f, 220f);
        _settings.ToneMixPct = Math.Clamp(env.WetMix * 100f, 0f, 100f);
        _settings.ToneGainDb = Math.Clamp(env.OutputGainDb, -12f, 12f);
        _settings.ToneGateDb = Math.Clamp(env.GateThresholdDb, -80f, -20f);
        if (env.WhiteBox != null)
            _settings.WhiteBox = env.WhiteBox;
        if (env.SignalChain != null)
            _settings.SignalChain = env.SignalChain.Clone();
        if (env.SavedPresets != null)
            _settings.SavedPresets = env.SavedPresets;
        if (env.GuitarProfiles != null)
            _settings.GuitarProfiles = env.GuitarProfiles;
        if (env.ActiveGuitarProfileId != null)
            _settings.ActiveGuitarProfileId = env.ActiveGuitarProfileId;

        SyncVisualizerCabIr();
        _wasIdleSettled = false;

        var chain = _kernel.Processor.Chain.GetConfigSnapshot();
        EmitEvent("signalChainChanged", new PluginSignalChainChangedDto
        {
            SignalChain = chain,
            SavedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            Summary = $"DAW 상태 복원: {chain.PresetName}",
        }, PluginJsonContext.Default.PluginSignalChainChangedDto);

        EmitEvent("tonesChanged", new PluginTonesChangedDto
        {
            Tones = ToModelDtos(_models),
            SelectedToneId = _kernel.Processor.ActiveProfile?.Id ?? "",
            ToneMode = (int)_kernel.Processor.Mode,
        }, PluginJsonContext.Default.PluginTonesChangedDto);

        return true;
    }

    public void TickUi()
    {
        if (_kernel.TryConsumeDawParamSync())
        {
            EmitEvent("dawParamSync", BuildDawParamSyncEvent(), PluginJsonContext.Default.PluginDawParamSyncEventDto);
            _wasIdleSettled = false;
        }

        _kernel.ReadAndResetIoMeters(out float inPkL, out float inPkR, out bool rawInClip, out float pkL, out float pkR, out bool rawOutClip);
        long nowTick = Environment.TickCount64;
        if (rawInClip) _inClipHoldUntil = nowTick + 600;
        if (rawOutClip) _outClipHoldUntil = nowTick + 600;
        bool inClip = nowTick < _inClipHoldUntil;
        bool clip = nowTick < _outClipHoldUntil;

        _inMeterL = inPkL > _inMeterL ? inPkL : _inMeterL * 0.80f;
        _inMeterR = inPkR > _inMeterR ? inPkR : _inMeterR * 0.80f;
        if (_inMeterL < 0.0002f) _inMeterL = 0f;
        if (_inMeterR < 0.0002f) _inMeterR = 0f;

        if (pkL > _meterL) _meterL = pkL;
        else _meterL *= 0.80f;
        if (pkR > _meterR) _meterR = pkR;
        else _meterR *= 0.80f;
        if (_meterL < 0.0002f) _meterL = 0f;
        if (_meterR < 0.0002f) _meterR = 0f;

        bool running = _kernel.IsRunning;
        var looper = _kernel.Processor.Looper.GetSnapshot();
        var recorder = _kernel.Recorder.GetSnapshot();
        bool looperActive = _kernel.Processor.Looper.State is LooperState.Recording or LooperState.Playing or LooperState.Overdubbing;
        bool settled = !running && !_guitarAnalyzer.IsActive && !recorder.Recording && !recorder.ReampPlaying && !looperActive && _meterL == 0f && _meterR == 0f && _inMeterL == 0f && _inMeterR == 0f && !clip && !inClip;
        if (settled && _wasIdleSettled)
            return;
        _wasIdleSettled = settled;

        GuitarAnalysisLiveStateDto? guitarAnalysis = null;
        if (_guitarAnalyzer.IsActive)
        {
            if (running)
            {
                int sr = _kernel.EffectiveSampleRate > 0 ? _kernel.EffectiveSampleRate : 48000;
                int feedLen = Math.Min(1024, Math.Max(256, sr / 30));
                _kernel.CopyRecentDrySamples(_guitarChunk.AsSpan(0, feedLen));
                if ((_guitarFeedSkip++ & 1) == 0)
                    _guitarAnalyzer.FeedDrySamples(_guitarChunk.AsSpan(0, feedLen), sr);
            }
            guitarAnalysis = _guitarAnalyzer.GetLiveState();
        }

        var vis = _visualizer.UpdateFromRouter(_kernel);
        int effSr = _kernel.EffectiveSampleRate > 0 ? _kernel.EffectiveSampleRate : 48000;
        double blockMs = Math.Round(_kernel.MaxBlockSize * 1000.0 / effSr, 1);

        var frame = new PluginTelemetryDataDto
        {
            Running = running,
            Busy = false,
            DirectDuplex = true,
            MeterL = MathF.Round(_meterL, 4),
            MeterR = MathF.Round(_meterR, 4),
            Clip = clip,
            InMeterL = MathF.Round(_inMeterL, 4),
            InMeterR = MathF.Round(_inMeterR, 4),
            InClip = inClip,
            GuitarAnalysis = guitarAnalysis,
            Latency = new PluginLatencyDto
            {
                InMs = blockMs,
                RingMs = 0.0,
                OutMs = blockMs,
                TotalMs = blockMs,
            },
            Counters = new PluginCountersDto
            {
                Padded = 0,
                Dropped = 0,
                Lost = 0,
                SampleRate = effSr,
            },
            Backing = new PluginBackingStateDto(false, false, true, 0.8f, 0, 0, "", ""),
            Looper = looper,
            Recorder = recorder,
            Vis = new PluginVisDto
            {
                Running = vis.Running,
                HasSignal = vis.HasSignal,
                HasIrCurve = vis.HasIrCurve,
                PeakDb = vis.PeakDb,
                Bands = vis.Bands,
                PeakCaps = vis.PeakCaps,
                Wave = vis.Wave,
                IrCurve = vis.IrCurve,
                Tuner = vis.Tuner,
            },
        };

        EmitEvent("telemetry", frame, PluginJsonContext.Default.PluginTelemetryDataDto);
    }

    public async void HandleWebMessage(string rawJson)
    {
        string? reqId = null;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            reqId = root.TryGetProperty("reqId", out var idEl) ? idEl.GetString() : null;
            string action = root.GetProperty("action").GetString() ?? "";
            JsonElement payload = root.TryGetProperty("payload", out var plEl) ? plEl : default;

            JsonElement? dataEl = action switch
            {
                "init" => SerializeEl(BuildInitSnapshot(), PluginJsonContext.Default.PluginInitSnapshotDto),
                "paramGesture" => SerializeEl(HandleParamGesture(payload), PluginJsonContext.Default.PluginOkResDto),
                "setHostBpmSync" => SerializeEl(HandleSetHostBpmSync(payload), PluginJsonContext.Default.PluginDawHostInfoDto),
                "refreshAudioDevices" => SerializeEl(BuildEmptyAudioSnapshot(), PluginJsonContext.Default.PluginAudioDevicesSnapshotDto),
                "refreshMidiDevices" => SerializeEl(BuildMidiSnapshot(), PluginJsonContext.Default.PluginMidiSnapshotDto),
                "updateToneParams" => SerializeEl(UpdateToneParams(payload), PluginJsonContext.Default.PluginOkResDto),
                "updateWhiteBoxParams" => SerializeEl(UpdateWhiteBoxParams(payload), PluginJsonContext.Default.PluginWhiteBoxResDto),
                "updateSignalChain" => SerializeEl(UpdateSignalChain(payload), PluginJsonContext.Default.PluginUpdateChainResDto),
                "setIoGain" => SerializeEl(HandleSetIoGain(payload), PluginJsonContext.Default.PluginIoGainResDto),
                "saveUserPreset" => SerializeEl(SaveUserPreset(payload), PluginJsonContext.Default.PluginSavedPresetsResDto),
                "deleteUserPreset" => SerializeEl(DeleteUserPreset(GetStr(payload, "presetId")), PluginJsonContext.Default.PluginSavedPresetsResDto),
                "selectTone" => SerializeEl(SelectTone(GetStr(payload, "id")), PluginJsonContext.Default.PluginSelectToneResDto),
                "deleteTone" => SerializeEl(DeleteTone(GetStr(payload, "id")), PluginJsonContext.Default.PluginDeleteToneResDto),
                "importNamFile" => SerializeEl(ImportNamFile(payload), PluginJsonContext.Default.PluginImportNamResDto),
                "exportNamFile" => SerializeEl(ExportNamFile(payload), PluginJsonContext.Default.PluginExportPathResDto),
                "loadCustomCabIr" => SerializeEl(LoadCustomCabIr(payload), PluginJsonContext.Default.PluginCabIrResDto),
                "clearCustomCabIr" => SerializeEl(ClearCustomCabIr(), PluginJsonContext.Default.PluginCabIrResDto),
                "setTunerConfig" => SerializeEl(SetTunerConfig(payload), PluginJsonContext.Default.PluginTunerConfigResDto),
                "startGuitarAnalysis" => SerializeEl(StartGuitarAnalysis(payload), PluginJsonContext.Default.GuitarAnalysisLiveStateDto),
                "cancelGuitarAnalysis" => SerializeEl(CancelGuitarAnalysis(), PluginJsonContext.Default.GuitarAnalysisLiveStateDto),
                "skipGuitarAnalysisStep" => SerializeEl(SkipGuitarAnalysisStep(), PluginJsonContext.Default.GuitarAnalysisLiveStateDto),
                "saveGuitarProfile" => SerializeEl(SaveGuitarProfile(payload), PluginJsonContext.Default.PluginGuitarProfilesResDto),
                "selectGuitarProfile" => SerializeEl(SelectGuitarProfile(GetStr(payload, "id")), PluginJsonContext.Default.PluginGuitarProfilesResDto),
                "deleteGuitarProfile" => SerializeEl(DeleteGuitarProfile(GetStr(payload, "id")), PluginJsonContext.Default.PluginGuitarProfilesResDto),
                "applyGuitarCalibration" => SerializeEl(ApplyGuitarCalibration(GetStr(payload, "id")), PluginJsonContext.Default.PluginApplyGuitarCalResDto),
                "generateAiTone" => SerializeEl(GenerateAiToneFromUi(payload), PluginJsonContext.Default.PluginAiToneResDto),
                "looperCommand" => SerializeEl(HandleLooperCommand(payload), PluginJsonContext.Default.LooperSnapshotDto),
                "recorderCommand" => SerializeEl(HandleRecorderCommand(payload), PluginJsonContext.Default.RecorderReampSnapshotDto),
                "exportNrigBundle" => SerializeEl(ExportNrigBundle(payload), PluginJsonContext.Default.PluginExportNrigResDto),
                "importNrigBundle" => SerializeEl(ImportNrigBundle(payload), PluginJsonContext.Default.PluginImportNrigResDto),
                "importDroppedFile" => SerializeEl(ImportDroppedFile(payload), PluginJsonContext.Default.PluginImportNrigResDto),
                "getToneHubCatalog" => SerializeEl(GetToneHubCatalog(), PluginJsonContext.Default.PluginToneHubCatalogResDto),
                "installToneHubItem" => SerializeEl(InstallToneHubItem(payload), PluginJsonContext.Default.PluginToneHubInstallResDto),
                "installToneHubUrl" => SerializeEl(await InstallToneHubUrlAsync(payload), PluginJsonContext.Default.PluginImportNrigResDto),
                "getCommunityHub" => SerializeEl(await GetCommunityHubAsync(payload), PluginJsonContext.Default.PluginCommunityHubResDto),
                "installCommunityGear" => SerializeEl(await InstallCommunityGearAsync(payload), PluginJsonContext.Default.PluginInstallCommunityGearResDto),
                "runBenchmarkSuite" => SerializeEl(RunBenchmarkSuite(payload), PluginJsonContext.Default.RigBenchmarkReportDto),
                "updateOnboardingState" => SerializeEl(UpdateOnboardingState(payload), PluginJsonContext.Default.PluginOkResDto),
                "openExternalUrl" => SerializeEl(OpenExternalUrl(GetStr(payload, "url")), PluginJsonContext.Default.PluginOkResDto),
                "setWindowTheme" => SerializeEl(new PluginOkResDto(), PluginJsonContext.Default.PluginOkResDto),
                "getSanitizedLogsPreview" => SerializeEl(new PluginSanitizedLogsDto
                {
                    Logs = LogSanitizer.GetRecentLogsText(250),
                    PendingCount = 0,
                }, PluginJsonContext.Default.PluginSanitizedLogsDto),
                _ => throw new InvalidOperationException($"플러그인 모드에서 지원되지 않는 액션: {action}"),
            };

            if (reqId != null)
            {
                SendRaw(new PluginRpcResponseDto
                {
                    Type = "response",
                    ReqId = reqId,
                    Ok = true,
                    Data = dataEl,
                }, PluginJsonContext.Default.PluginRpcResponseDto);
            }
        }
        catch (Exception ex)
        {
            if (reqId != null)
            {
                SendRaw(new PluginRpcResponseDto
                {
                    Type = "response",
                    ReqId = reqId,
                    Ok = false,
                    Error = ex.Message,
                }, PluginJsonContext.Default.PluginRpcResponseDto);
            }
            else
            {
                EmitLog($"[플러그인 브리지 오류] {ex.Message}");
            }
        }
    }

    private async Task<PluginCommunityHubResDto> GetCommunityHubAsync(JsonElement payload)
    {
        string cat = GetStr(payload, "category") ?? "all";
        string q = GetStr(payload, "query") ?? "";
        string sort = GetStr(payload, "sort") ?? "downloads";
        var (items, source) = await _cloud.GetCommunityHubAsync(_settings, cat, q, sort);
        return new PluginCommunityHubResDto { Items = items, Source = source };
    }

    private async Task<PluginInstallCommunityGearResDto> InstallCommunityGearAsync(JsonElement payload)
    {
        var item = JsonSerializer.Deserialize(payload.GetRawText(), PluginJsonContext.Default.CommunityGearItemDto)
            ?? throw new InvalidOperationException("Invalid community gear payload.");

        var installed = await _cloud.InstallCommunityGearAsync(_settings, item);
        AntigravityToneArchitect.RegisterCustomDevice(installed);
        var allDevices = CustomDeviceRegistry.GetAll();
        EmitEvent("customDevicesChanged", new PluginCustomDevicesChangedDto
        {
            Devices = allDevices,
            CustomDevices = allDevices,
            AddedDevice = installed,
            Summary = $"커뮤니티 기어 설치: {installed.OriginalName}",
        }, PluginJsonContext.Default.PluginCustomDevicesChangedDto);
        EmitLog($"[Community Hub] '{installed.OriginalName}' 설치 및 SPICE 검증 완료");
        return new PluginInstallCommunityGearResDto
        {
            Ok = true,
            AddedDevice = installed,
            CustomDevices = allDevices,
        };
    }

    private PluginInitSnapshotDto BuildInitSnapshot()
    {
        var active = _kernel.Processor.ActiveProfile;
        string fmtLower = string.Equals(_kernel.HostFormat, "CLAP", StringComparison.OrdinalIgnoreCase) ? "clap" : "vst3";
        return new PluginInitSnapshotDto
        {
            HostMode = fmtLower,
            DawHost = BuildDawHostInfo(),
            Settings = _settings,
            Presets = Presets,
            Audio = BuildEmptyAudioSnapshot(),
            Tones = ToModelDtos(_models),
            SelectedToneId = active?.Id ?? "",
            CustomCabIrName = _kernel.Processor.CustomCabIrName,
            WhiteBox = _kernel.Processor.WhiteBox.GetParamsSnapshot(),
            SignalChain = _kernel.Processor.Chain.GetConfigSnapshot(),
            IoGainLock = _settings.IoGainLock,
            SavedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            GuitarProfiles = _settings.GuitarProfiles ?? new List<GuitarProfileDto>(),
            ActiveGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
            TunerMute = _kernel.TunerMute,
            TunerRefA4Hz = _visualizer.TunerRefA4Hz,
            Backing = new PluginBackingStateDto(false, false, true, 0.8f, 0, 0, "", ""),
            Looper = _kernel.Processor.Looper.GetSnapshot(),
            Recorder = _kernel.Recorder.GetSnapshot(),
            Midi = BuildMidiSnapshot(),
            CustomDevices = CustomDeviceRegistry.GetAll(),
            IsAudioRunning = _kernel.IsRunning,
            AppVersion = OtaUpdateService.CurrentVersion,
            CloudState = _cloud.GetState(_settings),
        };
    }

    private PluginDawHostInfoDto BuildDawHostInfo() => new()
    {
        IsPluginHost = true,
        Format = _kernel.HostFormat,
        SampleRate = _kernel.EffectiveSampleRate,
        MaxBlockSize = _kernel.MaxBlockSize,
        ParameterCount = DawParameterCatalog.TotalParameterCount,
        HostBpm = _kernel.HostBpm,
        HostBpmSync = _kernel.HostBpmSync,
    };

    private static PluginAudioDevicesSnapshotDto BuildEmptyAudioSnapshot() => new()
    {
        AsioDrivers = Array.Empty<string>(),
        WasapiDevices = new[] { new PluginAudioDeviceOptionDto { Id = "daw", Label = "VST3 Host Audio Bus" } },
        WasapiInputDevices = new[] { new PluginAudioDeviceOptionDto { Id = "daw", Label = "VST3 Host Input Bus" } },
        InitialDriver = null,
        InitialCaps = null,
    };

    private PluginMidiSnapshotDto BuildMidiSnapshot() => new()
    {
        Devices = Array.Empty<string>(),
        ActiveDevice = "",
        Connected = false,
        Bindings = _settings.MidiBindings ?? new List<MidiBindingDto>(),
        Learning = null,
    };

    private PluginDawParamSyncEventDto BuildDawParamSyncEvent()
    {
        var chain = _kernel.Processor.Chain.GetConfigSnapshot();
        _settings.SignalChain = chain.Clone();
        return new PluginDawParamSyncEventDto
        {
            SignalChain = chain,
            HostBpm = _kernel.HostBpm,
            HostBpmSync = _kernel.HostBpmSync,
            InputGainDb = chain.InputGainDb,
            OutputGainDb = chain.OutputGainDb,
            GateThresholdDb = _kernel.Processor.GateThresholdDb,
            DriveMultiplier = _kernel.Processor.DriveMultiplier,
            WetMix = _kernel.Processor.WetMix * 100f,
        };
    }

    private PluginOkResDto HandleParamGesture(JsonElement payload)
    {
        string phaseStr = (GetStr(payload, "phase") ?? "change").ToLowerInvariant();
        int phase = phaseStr switch
        {
            "begin" => 0,
            "end" => 2,
            _ => 1,
        };

        uint paramId;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("paramId", out var idEl) && idEl.TryGetUInt32(out uint directId))
        {
            paramId = directId;
        }
        else
        {
            string kind = (GetStr(payload, "kind") ?? "slotParam").ToLowerInvariant();
            paramId = kind switch
            {
                "slotbypass" => DawParameterCatalog.SlotBypassParamId(GetInt(payload, "slotIndex", 0)),
                "slotparam" => DawParameterCatalog.SlotKnobParamId(GetInt(payload, "slotIndex", 0), GetInt(payload, "paramIndex", 0)),
                "scene" => (uint)DawParamId.ActiveScene,
                "tempo" => (uint)DawParamId.TempoBpm,
                "routing" => (uint)DawParamId.RoutingMode,
                "parallelmix" => (uint)DawParamId.ParallelMix,
                "inputgain" => (uint)DawParamId.InputGainDb,
                "outputgain" => (uint)DawParamId.OutputGainDb,
                "gate" => (uint)DawParamId.GateThresholdDb,
                "drive" => (uint)DawParamId.DriveMultiplier,
                "wetmix" => (uint)DawParamId.WetMix,
                _ => uint.MaxValue,
            };
        }

        if (paramId >= DawParameterCatalog.TotalParameterCount)
            return new PluginOkResDto { Ok = false };

        double norm;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("normalized", out var nEl) && nEl.TryGetDouble(out double dNorm))
        {
            norm = Math.Clamp(dNorm, 0.0, 1.0);
        }
        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out var vEl) && vEl.TryGetSingle(out float plain))
        {
            norm = _kernel.PlainToNormalizedLive(paramId, plain);
        }
        else
        {
            norm = _kernel.GetParamNormalized(paramId);
        }

        if (phase != 0 || (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out _)))
            _kernel.SetParamFromUi(paramId, (float)norm);

        _onHostParamGesture?.Invoke(paramId, phase, norm);
        _wasIdleSettled = false;
        return new PluginOkResDto { Ok = true };
    }

    private PluginDawHostInfoDto HandleSetHostBpmSync(JsonElement payload)
    {
        bool enabled = GetBool(payload, "enabled", !_kernel.HostBpmSync);
        _kernel.HostBpmSync = enabled;
        return BuildDawHostInfo();
    }

    private PluginOkResDto UpdateToneParams(JsonElement payload)
    {
        int mode = Math.Clamp(GetInt(payload, "mode", _settings.ToneMode), 0, 6);
        float drivePct = Math.Clamp(GetFloat(payload, "drivePct", _settings.ToneDrivePct), 20f, 220f);
        float mixPct = Math.Clamp(GetFloat(payload, "mixPct", _settings.ToneMixPct), 0f, 100f);
        float gainDb = Math.Clamp(GetFloat(payload, "gainDb", _settings.ToneGainDb), -12f, 12f);
        float gateDb = Math.Clamp(GetFloat(payload, "gateDb", _settings.ToneGateDb), -80f, -20f);

        _settings.ToneMode = mode;
        _settings.ToneDrivePct = drivePct;
        _settings.ToneMixPct = mixPct;
        _settings.ToneGainDb = gainDb;
        _settings.ToneGateDb = gateDb;
        _settings.Save();

        var eff = (ToneMode)mode;
        if (_kernel.Processor.ActiveProfile is null && (eff == ToneMode.FullRig || eff == ToneMode.AmpHeadOnly))
            eff = ToneMode.SignalChain;

        _kernel.Processor.Mode = eff;
        _kernel.Processor.DriveMultiplier = drivePct / 100f;
        _kernel.Processor.WetMix = mixPct / 100f;
        _kernel.Processor.OutputGainDb = gainDb;
        _kernel.Processor.GateThresholdDb = gateDb;
        _kernel.SyncParamsFromEngine();
        SyncVisualizerCabIr();
        return new PluginOkResDto { Ok = true, ToneMode = (int)eff };
    }

    private PluginWhiteBoxResDto UpdateWhiteBoxParams(JsonElement payload)
    {
        var wb = (_settings.WhiteBox ?? new WhiteBoxParams()).Clone();
        wb.Channel = (WhiteBoxChannel)Math.Clamp(GetInt(payload, "channel", (int)wb.Channel), 0, 2);
        wb.Bright = GetBool(payload, "bright", wb.Bright);
        wb.TsEnabled = GetBool(payload, "tsEnabled", wb.TsEnabled);
        wb.TsDrive = Math.Clamp(GetFloat(payload, "tsDrive", wb.TsDrive), 0f, 10f);
        wb.TsTone = Math.Clamp(GetFloat(payload, "tsTone", wb.TsTone), 0f, 10f);
        wb.TsLevel = Math.Clamp(GetFloat(payload, "tsLevel", wb.TsLevel), 0f, 10f);
        wb.PreGain = Math.Clamp(GetFloat(payload, "preGain", wb.PreGain), 0f, 10f);
        wb.Bass = Math.Clamp(GetFloat(payload, "bass", wb.Bass), 0f, 10f);
        wb.Mid = Math.Clamp(GetFloat(payload, "mid", wb.Mid), 0f, 10f);
        wb.Treble = Math.Clamp(GetFloat(payload, "treble", wb.Treble), 0f, 10f);
        wb.Master = Math.Clamp(GetFloat(payload, "master", wb.Master), 0f, 10f);
        wb.Sag = Math.Clamp(GetFloat(payload, "sag", wb.Sag), 0f, 10f);
        wb.Presence = Math.Clamp(GetFloat(payload, "presence", wb.Presence), 0f, 10f);
        wb.Depth = Math.Clamp(GetFloat(payload, "depth", wb.Depth), 0f, 10f);
        wb.PowerAmpEnabled = GetBool(payload, "powerAmpEnabled", wb.PowerAmpEnabled);

        _settings.WhiteBox = wb;
        _settings.Save();
        _kernel.Processor.WhiteBox.UpdateParams(wb);
        return new PluginWhiteBoxResDto { Ok = true, WhiteBox = _kernel.Processor.WhiteBox.GetParamsSnapshot() };
    }

    private PluginIoGainResDto HandleSetIoGain(JsonElement payload)
    {
        float? inDb = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("inputGainDb", out var iEl) && iEl.TryGetSingle(out float iVal)
            ? MathF.Round(Math.Clamp(iVal, -24f, 24f), 2)
            : null;
        float? outDb = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("outputGainDb", out var oEl) && oEl.TryGetSingle(out float oVal)
            ? MathF.Round(Math.Clamp(oVal, -24f, 24f), 2)
            : null;
        bool commit = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("commit", out var cEl) && cEl.ValueKind == JsonValueKind.True;

        bool lockChanged = false;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("ioGainLock", out var lEl) &&
            lEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            bool nextLock = lEl.ValueKind == JsonValueKind.True;
            lockChanged = _settings.IoGainLock != nextLock;
            _settings.IoGainLock = nextLock;
        }

        _kernel.SetIoGainFromUi(inDb, outDb);
        _settings.SignalChain ??= new SignalChainConfigDto();
        if (inDb.HasValue) _settings.SignalChain.InputGainDb = inDb.Value;
        if (outDb.HasValue) _settings.SignalChain.OutputGainDb = outDb.Value;
        if (commit || lockChanged)
            _settings.Save();

        return new PluginIoGainResDto
        {
            Ok = true,
            InputGainDb = _settings.SignalChain.InputGainDb,
            OutputGainDb = _settings.SignalChain.OutputGainDb,
            IoGainLock = _settings.IoGainLock,
        };
    }

    private PluginUpdateChainResDto UpdateSignalChain(JsonElement payload)
    {
        SignalChainConfigDto? cfg = null;
        if (payload.ValueKind == JsonValueKind.Object)
            cfg = JsonSerializer.Deserialize(payload.GetRawText(), PluginJsonContext.Default.SignalChainConfigDto);
        cfg ??= new SignalChainConfigDto();
        var clean = cfg.Clone();
        _settings.SignalChain = clean;
        _settings.ToneMode = (int)ToneMode.SignalChain;
        _settings.Save();

        _kernel.LoadChainConfig(clean);
        SyncVisualizerCabIr();
        return new PluginUpdateChainResDto { Ok = true, SignalChain = _kernel.Processor.Chain.GetConfigSnapshot() };
    }

    private PluginSavedPresetsResDto SaveUserPreset(JsonElement payload)
    {
        SignalChainConfigDto? cfg = null;
        if (payload.ValueKind == JsonValueKind.Object)
            cfg = JsonSerializer.Deserialize(payload.GetRawText(), PluginJsonContext.Default.SignalChainConfigDto);
        cfg ??= _kernel.Processor.Chain.GetConfigSnapshot();
        var clean = cfg.Clone();
        if (string.IsNullOrWhiteSpace(clean.PresetId))
            clean.PresetId = $"U{DateTime.Now:HHmmss}";

        _settings.SavedPresets ??= new List<SignalChainConfigDto>();
        int idx = _settings.SavedPresets.FindIndex(p => string.Equals(p.PresetId, clean.PresetId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _settings.SavedPresets[idx] = clean;
        else _settings.SavedPresets.Add(clean);

        _settings.SignalChain = clean;
        _settings.Save();
        EmitLog($"[프리셋] '{clean.PresetId} {clean.PresetName}' 저장됨");
        return new PluginSavedPresetsResDto { Ok = true, SavedPresets = _settings.SavedPresets };
    }

    private PluginSavedPresetsResDto DeleteUserPreset(string? presetId)
    {
        _settings.SavedPresets ??= new List<SignalChainConfigDto>();
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            _settings.SavedPresets.RemoveAll(p => string.Equals(p.PresetId, presetId, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
        }
        return new PluginSavedPresetsResDto { Ok = true, SavedPresets = _settings.SavedPresets };
    }

    private PluginSelectToneResDto SelectTone(string? id)
    {
        var prof = string.IsNullOrWhiteSpace(id) ? null : _models.FirstOrDefault(t => t.Id == id);
        _kernel.Processor.ActiveProfile = prof;
        int nextMode = Math.Clamp(_settings.ToneMode, 0, 6);
        if (nextMode != (int)ToneMode.SignalChain)
        {
            if (prof != null && (nextMode == (int)ToneMode.Bypass || nextMode == (int)ToneMode.WhiteBoxRig))
                nextMode = (int)ToneMode.FullRig;
            if (prof is null && (nextMode == (int)ToneMode.FullRig || nextMode == (int)ToneMode.AmpHeadOnly))
                nextMode = (int)ToneMode.WhiteBoxRig;
        }
        _kernel.Processor.Mode = (ToneMode)nextMode;
        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        _settings.ToneMode = nextMode;
        _settings.SelectedToneId = prof?.Id;
        _settings.Save();
        return new PluginSelectToneResDto
        {
            SelectedToneId = prof?.Id ?? "",
            CustomCabIrName = _kernel.Processor.CustomCabIrName,
            ToneMode = nextMode,
        };
    }

    private PluginDeleteToneResDto DeleteTone(string? id)
    {
        var prof = string.IsNullOrWhiteSpace(id) ? null : _models.FirstOrDefault(t => t.Id == id);
        if (prof is null) return new PluginDeleteToneResDto { Deleted = false };

        ToneProfileStore.Delete(prof);
        _models = ToneProfileStore.LoadAll();
        _kernel.Processor.SetModelLibrary(_models);
        var next = _models.FirstOrDefault();
        _kernel.Processor.ActiveProfile = next;

        int nextMode = Math.Clamp(_settings.ToneMode, 0, 6);
        if (nextMode != (int)ToneMode.SignalChain && next is null && (nextMode == (int)ToneMode.FullRig || nextMode == (int)ToneMode.AmpHeadOnly))
            nextMode = (int)ToneMode.WhiteBoxRig;
        _kernel.Processor.Mode = (ToneMode)nextMode;
        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        _settings.ToneMode = nextMode;
        _settings.SelectedToneId = next?.Id;
        _settings.Save();

        var dtos = ToModelDtos(_models);
        EmitEvent("tonesChanged", new PluginTonesChangedDto
        {
            Tones = dtos,
            SelectedToneId = next?.Id ?? "",
            ToneMode = nextMode,
        }, PluginJsonContext.Default.PluginTonesChangedDto);
        EmitLog($"[모델] '{prof.Name}' 삭제됨");
        return new PluginDeleteToneResDto
        {
            Deleted = true,
            Tones = dtos,
            SelectedToneId = next?.Id ?? "",
            ToneMode = nextMode,
        };
    }

    private PluginImportNamResDto ImportNamFile(JsonElement payload)
    {
        string? namPath = GetStr(payload, "namPath");
        string? customName = GetStr(payload, "name");
        if (string.IsNullOrWhiteSpace(namPath))
        {
            namPath = ShowWin32OpenFileDialog(
                "표준 .nam (Neural Amp Modeler) 모델 불러오기",
                "Neural Amp Modeler (.nam)\0*.nam;*.json\0All Files\0*.*\0\0");
            if (string.IsNullOrWhiteSpace(namPath))
                return new PluginImportNamResDto { Cancelled = true };
        }

        var loaded = ToneProfileStore.ImportNamFile(namPath, customName);
        _models = ToneProfileStore.LoadAll();
        loaded = _models.FirstOrDefault(t => t.Id == loaded.Id) ?? loaded;

        _kernel.Processor.SetModelLibrary(_models);
        _kernel.Processor.ActiveProfile = loaded;

        int slotIndex = GetInt(payload, "slotIndex", -1);
        SignalChainConfigDto? updatedChain = null;
        if (slotIndex >= 0 && slotIndex < SignalChainConfigDto.SlotCount && _settings.SignalChain?.Slots != null && slotIndex < _settings.SignalChain.Slots.Count)
        {
            var slot = _settings.SignalChain.Slots[slotIndex];
            if (string.Equals(slot.ModelId, "nam_capture", StringComparison.OrdinalIgnoreCase))
            {
                slot.NamModelId = loaded.Id;
                _kernel.LoadChainConfig(_settings.SignalChain);
                updatedChain = _kernel.Processor.Chain.GetConfigSnapshot();
            }
        }

        int nextMode = _settings.ToneMode == (int)ToneMode.SignalChain
            ? (int)ToneMode.SignalChain
            : _settings.ToneMode == (int)ToneMode.HybridPreampNam
                ? (int)ToneMode.HybridPreampNam
                : (_kernel.Processor.HasCustomCabIr ? (int)ToneMode.AmpHeadOnly : (int)ToneMode.FullRig);
        _kernel.Processor.Mode = (ToneMode)nextMode;

        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        _settings.ToneMode = nextMode;
        _settings.SelectedToneId = loaded.Id;
        _settings.Save();

        var dtos = ToModelDtos(_models);
        EmitEvent("tonesChanged", new PluginTonesChangedDto
        {
            Tones = dtos,
            SelectedToneId = loaded.Id,
            ToneMode = nextMode,
        }, PluginJsonContext.Default.PluginTonesChangedDto);

        if (updatedChain != null)
        {
            EmitEvent("signalChainChanged", new PluginSignalChainChangedDto
            {
                SignalChain = updatedChain,
                SavedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
                Summary = $"슬롯 #{slotIndex + 1} NAM 할당: {loaded.Name}",
            }, PluginJsonContext.Default.PluginSignalChainChangedDto);
        }

        EmitLog($"[모델] .nam 로드 완료 ({loaded.Architecture}): {loaded.Name} ({loaded.SampleRate} Hz)");
        return new PluginImportNamResDto
        {
            Cancelled = false,
            Profile = ToModelDtos(new[] { loaded })[0],
            Tones = dtos,
            SelectedToneId = loaded.Id,
            ToneMode = nextMode,
            SignalChain = updatedChain,
        };
    }

    private PluginExportPathResDto ExportNamFile(JsonElement payload)
    {
        string? id = GetStr(payload, "toneId") ?? _kernel.Processor.ActiveProfile?.Id;
        var prof = string.IsNullOrWhiteSpace(id) ? _kernel.Processor.ActiveProfile : _models.FirstOrDefault(t => t.Id == id);
        if (prof is null)
            throw new InvalidOperationException("내보낼 .nam 모델이 선택되지 않았습니다.");

        string? savePath = ShowWin32SaveFileDialog(
            "표준 .nam 파일 내보내기",
            "Neural Amp Modeler (.nam)\0*.nam\0\0",
            $"{prof.Name}.nam",
            "nam");
        if (string.IsNullOrWhiteSpace(savePath))
            return new PluginExportPathResDto { Cancelled = true };

        ToneProfileStore.ExportNamFile(prof, savePath);
        EmitLog($"[내보내기] 표준 .nam 저장됨: {savePath}");
        return new PluginExportPathResDto { Cancelled = false, Path = savePath };
    }

    private PluginCabIrResDto LoadCustomCabIr(JsonElement payload)
    {
        string? path = GetStr(payload, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = ShowWin32OpenFileDialog(
                "외부 캐비넷 IR (.wav) 선택",
                "Cabinet IR WAV\0*.wav\0All Files\0*.*\0\0");
            if (string.IsNullOrWhiteSpace(path))
            {
                return new PluginCabIrResDto
                {
                    Cancelled = true,
                    Loaded = _kernel.Processor.HasCustomCabIr,
                    CustomCabIrName = _kernel.Processor.CustomCabIrName,
                };
            }
        }

        var (name, taps) = _kernel.Processor.LoadCustomCabIr(path);
        _settings.CustomCabIrPath = path;
        _settings.Save();
        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        EmitLog($"[IR] 외부 캐비넷 IR 로드: {name} ({taps} taps)");
        return new PluginCabIrResDto
        {
            Cancelled = false,
            Loaded = true,
            CustomCabIrName = name,
            Taps = taps,
        };
    }

    private PluginCabIrResDto ClearCustomCabIr()
    {
        _kernel.Processor.ClearCustomCabIr();
        _settings.CustomCabIrPath = null;
        _settings.Save();
        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        EmitLog("[IR] 외부 캐비넷 IR 해제 — 내장 4x12 V30 IR 복귀");
        return new PluginCabIrResDto { Cancelled = false, Loaded = false, CustomCabIrName = "" };
    }

    private PluginTunerConfigResDto SetTunerConfig(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("mute", out var mEl) && (mEl.ValueKind == JsonValueKind.True || mEl.ValueKind == JsonValueKind.False))
                _kernel.TunerMute = mEl.GetBoolean();
            if (payload.TryGetProperty("refA4Hz", out var aEl) && aEl.TryGetSingle(out float a4))
                _visualizer.TunerRefA4Hz = Math.Clamp(a4, 430f, 450f);
        }
        return new PluginTunerConfigResDto
        {
            Mute = _kernel.TunerMute,
            RefA4Hz = _visualizer.TunerRefA4Hz,
        };
    }

    private GuitarAnalysisLiveStateDto StartGuitarAnalysis(JsonElement payload)
    {
        string? name = GetStr(payload, "name");
        _wasIdleSettled = false;
        var st = _guitarAnalyzer.Start(name);
        EmitLog($"[기타 분석] 가이드 분석 시작: {name ?? "내 기타"}");
        return st;
    }

    private GuitarAnalysisLiveStateDto CancelGuitarAnalysis()
    {
        _guitarAnalyzer.Cancel();
        return _guitarAnalyzer.GetLiveState();
    }

    private GuitarAnalysisLiveStateDto SkipGuitarAnalysisStep()
    {
        _wasIdleSettled = false;
        return _guitarAnalyzer.SkipOrForceStep();
    }

    private PluginGuitarProfilesResDto SaveGuitarProfile(JsonElement payload)
    {
        GuitarProfileDto? prof = null;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("profile", out var pEl) && pEl.ValueKind == JsonValueKind.Object)
            prof = JsonSerializer.Deserialize(pEl.GetRawText(), PluginJsonContext.Default.GuitarProfileDto);
        prof ??= _guitarAnalyzer.CompletedProfile;
        if (prof is null)
            throw new InvalidOperationException("저장할 기타 분석 결과가 없습니다.");

        prof = prof.Clone();
        string? nameOverride = GetStr(payload, "name");
        if (!string.IsNullOrWhiteSpace(nameOverride)) prof.Name = nameOverride.Trim();
        if (string.IsNullOrWhiteSpace(prof.Id)) prof.Id = $"G{DateTime.Now:yyyyMMddHHmmss}";
        if (string.IsNullOrWhiteSpace(prof.CreatedAt)) prof.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        int idx = _settings.GuitarProfiles.FindIndex(g => string.Equals(g.Id, prof.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _settings.GuitarProfiles[idx] = prof;
        else _settings.GuitarProfiles.Insert(0, prof);

        _settings.ActiveGuitarProfileId = prof.Id;
        _settings.Save();
        _guitarAnalyzer.Cancel();

        var ev = new PluginGuitarProfilesResDto
        {
            Profile = prof,
            GuitarProfiles = _settings.GuitarProfiles,
            ActiveGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        };
        EmitEvent("guitarProfilesChanged", ev, PluginJsonContext.Default.PluginGuitarProfilesResDto);
        return ev;
    }

    private PluginGuitarProfilesResDto SelectGuitarProfile(string? id)
    {
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        if (!string.IsNullOrWhiteSpace(id) && _settings.GuitarProfiles.Any(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)))
            _settings.ActiveGuitarProfileId = id;
        else if (string.IsNullOrWhiteSpace(id))
            _settings.ActiveGuitarProfileId = null;
        _settings.Save();

        var ev = new PluginGuitarProfilesResDto
        {
            GuitarProfiles = _settings.GuitarProfiles,
            ActiveGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        };
        EmitEvent("guitarProfilesChanged", ev, PluginJsonContext.Default.PluginGuitarProfilesResDto);
        return ev;
    }

    private PluginGuitarProfilesResDto DeleteGuitarProfile(string? id)
    {
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        if (!string.IsNullOrWhiteSpace(id))
        {
            _settings.GuitarProfiles.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_settings.ActiveGuitarProfileId, id, StringComparison.OrdinalIgnoreCase))
                _settings.ActiveGuitarProfileId = _settings.GuitarProfiles.FirstOrDefault()?.Id;
            _settings.Save();
        }
        var ev = new PluginGuitarProfilesResDto
        {
            GuitarProfiles = _settings.GuitarProfiles,
            ActiveGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        };
        EmitEvent("guitarProfilesChanged", ev, PluginJsonContext.Default.PluginGuitarProfilesResDto);
        return ev;
    }

    private PluginApplyGuitarCalResDto ApplyGuitarCalibration(string? id)
    {
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        string? targetId = string.IsNullOrWhiteSpace(id) ? _settings.ActiveGuitarProfileId : id;
        var prof = string.IsNullOrWhiteSpace(targetId)
            ? null
            : _settings.GuitarProfiles.FirstOrDefault(g => string.Equals(g.Id, targetId, StringComparison.OrdinalIgnoreCase));
        if (prof is null)
            throw new InvalidOperationException("적용할 기타 프로파일을 먼저 선택하거나 분석하세요.");

        var chain = _kernel.Processor.Chain.GetConfigSnapshot();
        chain.InputGainDb = MathF.Round(Math.Clamp(prof.SuggestedInputTrimDb, -12f, 12f), 1);
        float gateDb = MathF.Round(Math.Clamp(prof.SuggestedGateDb, -80f, -20f), 1);
        _settings.SignalChain = chain.Clone();
        _settings.ToneGateDb = gateDb;
        _settings.Save();

        _kernel.Processor.GateThresholdDb = gateDb;
        _kernel.LoadChainConfig(chain);

        var updated = _kernel.Processor.Chain.GetConfigSnapshot();
        EmitEvent("signalChainChanged", new PluginSignalChainChangedDto
        {
            SignalChain = updated,
            SavedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            Summary = $"기타 보정 적용: {prof.Name} (Trim {chain.InputGainDb:+0.0;-0.0;0.0} dB, Gate {gateDb:0} dB)",
        }, PluginJsonContext.Default.PluginSignalChainChangedDto);

        return new PluginApplyGuitarCalResDto
        {
            ProfileName = prof.Name,
            InputGainDb = chain.InputGainDb,
            GateDb = gateDb,
            SignalChain = updated,
        };
    }

    private PluginAiToneResDto GenerateAiToneFromUi(JsonElement payload)
    {
        string prompt = GetStr(payload, "prompt") ?? "";
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("원하는 톤 설명을 입력하세요.");
        bool autoSave = GetBool(payload, "autoSave", false);

        var curChain = _kernel.Processor.Chain.GetConfigSnapshot();
        var activeGuitar = string.IsNullOrWhiteSpace(_settings.ActiveGuitarProfileId)
            ? null
            : _settings.GuitarProfiles?.FirstOrDefault(g => string.Equals(g.Id, _settings.ActiveGuitarProfileId, StringComparison.OrdinalIgnoreCase));
        var res = _toneArchitect.GenerateViaBuiltinArchitect(prompt, curChain, activeGuitar);

        var clean = res.Chain.Clone();
        _settings.ToneMode = (int)ToneMode.SignalChain;
        _settings.SignalChain = clean;
        if (autoSave)
        {
            _settings.SavedPresets ??= new List<SignalChainConfigDto>();
            int idx = _settings.SavedPresets.FindIndex(p => string.Equals(p.PresetId, clean.PresetId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _settings.SavedPresets[idx] = clean;
            else _settings.SavedPresets.Add(clean);
        }
        _settings.Save();
        _kernel.LoadChainConfig(clean);
        SyncVisualizerCabIr();

        var liveChain = _kernel.Processor.Chain.GetConfigSnapshot();
        var saved = _settings.SavedPresets ?? new List<SignalChainConfigDto>();
        EmitEvent("signalChainChanged", new PluginSignalChainChangedDto
        {
            SignalChain = liveChain,
            SavedPresets = saved,
            Summary = $"{res.PresetName} — {res.Summary}",
        }, PluginJsonContext.Default.PluginSignalChainChangedDto);

        return new PluginAiToneResDto
        {
            Ok = true,
            PresetName = res.PresetName,
            Summary = res.Summary,
            Highlights = res.GearHighlights,
            SignalChain = liveChain,
            SavedPresets = saved,
        };
    }

    private LooperSnapshotDto HandleLooperCommand(JsonElement payload)
    {
        var looper = _kernel.Processor.Looper;
        if (looper.LoopLengthFrames == 0 && looper.State == LooperState.Empty && _kernel.EffectiveSampleRate <= 0)
            looper.Prepare(48000, maxSeconds: 60);

        string cmd = (GetStr(payload, "cmd") ?? "").Trim();
        switch (cmd)
        {
            case "recDub": looper.TriggerRecordOrOverdub(); break;
            case "playStop": looper.TriggerPlayStop(); break;
            case "clear":
                if (_kernel.IsRunning) looper.Clear();
                else looper.ResetAllImmediate();
                break;
            case "undo": looper.Undo(); break;
            case "redo": looper.Redo(); break;
            case "halfSpeed": looper.HalfSpeed = GetBool(payload, "value", !looper.HalfSpeed); break;
            case "reverse": looper.Reverse = GetBool(payload, "value", !looper.Reverse); break;
            case "quantize": looper.QuantizeSync = GetBool(payload, "value", !looper.QuantizeSync); break;
            case "level":
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out var lvEl) && lvEl.TryGetSingle(out float lv))
                    looper.Level = lv;
                break;
            default:
                throw new InvalidOperationException($"알 수 없는 루퍼 명령: {cmd}");
        }

        _wasIdleSettled = false;
        var snap = looper.GetSnapshot();
        EmitEvent("looperStateChanged", snap, PluginJsonContext.Default.LooperSnapshotDto);
        return snap;
    }

    private RecorderReampSnapshotDto HandleRecorderCommand(JsonElement payload)
    {
        string cmd = (GetStr(payload, "cmd") ?? "").Trim();
        RecorderReampSnapshotDto snap = cmd switch
        {
            "startRec" => _kernel.Recorder.StartRecording(sessionName: GetStr(payload, "sessionName")),
            "stopRec" => _kernel.Recorder.StopRecording(),
            "toggleReamp" => _kernel.Recorder.SetReampState(enabled: GetBool(payload, "enabled", !_kernel.Recorder.IsReampEnabled)),
            "playPauseReamp" => _kernel.Recorder.SetReampState(playing: GetBool(payload, "playing", !_kernel.Recorder.IsReampPlaying)),
            "loopReamp" => _kernel.Recorder.SetReampState(loop: GetBool(payload, "loop", true)),
            "seekReamp" => _kernel.Recorder.SetReampState(seekSec: GetFloat(payload, "seekSec", 0f)),
            "loadReampFile" => LoadReampDialogOrPath(GetStr(payload, "path")),
            "openRecordingsFolder" => OpenRecordingsFolder(),
            _ => throw new InvalidOperationException($"알 수 없는 레코더 명령: {cmd}"),
        };

        _wasIdleSettled = false;
        EmitEvent("recorderStateChanged", snap, PluginJsonContext.Default.RecorderReampSnapshotDto);
        return snap;
    }

    private RecorderReampSnapshotDto LoadReampDialogOrPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = ShowWin32OpenFileDialog("리앰프할 Dry DI WAV 파일 선택", "WAV Audio\0*.wav\0All Files\0*.*\0\0");
            if (string.IsNullOrWhiteSpace(path))
                return _kernel.Recorder.GetSnapshot();
        }
        return _kernel.Recorder.LoadReampDryFile(path);
    }

    private RecorderReampSnapshotDto OpenRecordingsFolder()
    {
        try
        {
            Directory.CreateDirectory(_kernel.Recorder.RecordingsDirectory);
            Process.Start(new ProcessStartInfo { FileName = _kernel.Recorder.RecordingsDirectory, UseShellExecute = true });
        }
        catch { }
        return _kernel.Recorder.GetSnapshot();
    }

    private PluginExportNrigResDto ExportNrigBundle(JsonElement payload)
    {
        var chain = _kernel.Processor.Chain.GetConfigSnapshot();
        string safeTitle = string.Concat(chain.PresetName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "NeuralRig_Bundle";

        string? destPath = GetStr(payload, "path");
        if (string.IsNullOrWhiteSpace(destPath))
        {
            destPath = ShowWin32SaveFileDialog(
                ".nrig 통합 리그 번들 내보내기",
                "NeuralRig 통합 번들 (.nrig)\0*.nrig\0\0",
                $"{chain.PresetId}_{safeTitle}.nrig",
                "nrig");
            if (string.IsNullOrWhiteSpace(destPath))
                return new PluginExportNrigResDto { Cancelled = true };
        }

        GuitarProfileDto? activeGuitar = null;
        if (!string.IsNullOrWhiteSpace(_settings.ActiveGuitarProfileId) && _settings.GuitarProfiles != null)
            activeGuitar = _settings.GuitarProfiles.FirstOrDefault(g => string.Equals(g.Id, _settings.ActiveGuitarProfileId, StringComparison.OrdinalIgnoreCase));

        int sr = _kernel.EffectiveSampleRate > 0 ? _kernel.EffectiveSampleRate : 48000;
        var manifest = NrigBundleService.ExportBundle(
            destPath,
            chain,
            _kernel.Processor,
            _models,
            activeGuitar,
            author: GetStr(payload, "author"),
            description: GetStr(payload, "description"),
            sampleRate: sr);

        return new PluginExportNrigResDto
        {
            Cancelled = false,
            Path = destPath,
            Name = manifest.Name,
            NamCount = manifest.NamModels.Count,
            HasCabIr = manifest.CustomCabIr != null,
        };
    }

    private PluginImportNrigResDto ImportNrigBundle(JsonElement payload)
    {
        string? nrigPath = GetStr(payload, "path");
        if (string.IsNullOrWhiteSpace(nrigPath))
        {
            nrigPath = ShowWin32OpenFileDialog(
                ".nrig 통합 리그 번들 불러오기",
                "NeuralRig 통합 번들 (.nrig)\0*.nrig\0All Files\0*.*\0\0");
            if (string.IsNullOrWhiteSpace(nrigPath))
                return new PluginImportNrigResDto { Cancelled = true };
        }

        var res = NrigBundleService.ImportBundle(nrigPath, _kernel.Processor);
        return ApplyImportedRigAndNotify(res, $".nrig 번들 임포트: {res.SignalChain.PresetName}", saveAsUserPreset: true);
    }

    private PluginImportNrigResDto ImportDroppedFile(JsonElement payload)
    {
        string fileName = GetStr(payload, "fileName") ?? "dropped_file.nam";
        string b64 = GetStr(payload, "base64Data") ?? "";
        if (string.IsNullOrWhiteSpace(b64))
            throw new InvalidOperationException("드롭된 파일 데이터가 비어 있습니다.");

        int comma = b64.IndexOf(',');
        if (comma >= 0) b64 = b64[(comma + 1)..];
        byte[] bytes = Convert.FromBase64String(b64);

        var res = ToneHubService.ImportRawFileBytes(fileName, bytes, _kernel.Processor);
        return ApplyImportedRigAndNotify(res, $"드래그앤드롭 로드: {fileName}", saveAsUserPreset: Path.GetExtension(fileName).Equals(".nrig", StringComparison.OrdinalIgnoreCase));
    }

    private PluginToneHubCatalogResDto GetToneHubCatalog() => new()
    {
        Items = ToneHubService.GetCatalog(_models, _settings.SavedPresets, _kernel.Processor.CustomCabIrName).ToArray(),
        DawPlugin = DawPluginExporter.GetPluginInspectorInfo(),
    };

    private PluginToneHubInstallResDto InstallToneHubItem(JsonElement payload)
    {
        string id = GetStr(payload, "id") ?? throw new InvalidOperationException("Tone Hub 항목 ID가 필요합니다.");
        int slotIdx = GetInt(payload, "targetSlotIndex", -1);
        var res = ToneHubService.InstallHubItem(id, _kernel.Processor, slotIdx);

        var fakeImport = new NrigImportResultDto(
            new NrigManifestDto { Name = res.Item.Title, SignalChain = res.AppliedChain ?? _kernel.Processor.Chain.GetConfigSnapshot() },
            res.AppliedChain ?? _kernel.Processor.Chain.GetConfigSnapshot(),
            res.InstalledModel != null ? new List<ToneProfile> { res.InstalledModel } : new List<ToneProfile>(),
            res.CustomCabIrPath,
            res.CustomCabIrName,
            null);

        ApplyImportedRigAndNotify(
            fakeImport,
            res.Summary,
            saveAsUserPreset: string.Equals(res.Item.Category, "nrig", StringComparison.OrdinalIgnoreCase));

        return new PluginToneHubInstallResDto
        {
            Summary = res.Summary,
            Catalog = ToneHubService.GetCatalog(_models, _settings.SavedPresets, _kernel.Processor.CustomCabIrName).ToArray(),
        };
    }

    private async Task<PluginImportNrigResDto> InstallToneHubUrlAsync(JsonElement payload)
    {
        string url = GetStr(payload, "url") ?? throw new InvalidOperationException("다운로드할 URL을 입력하세요.");
        var res = await ToneHubService.InstallFromUrlAsync(url, _kernel.Processor, ct: _cts.Token).ConfigureAwait(false);
        return ApplyImportedRigAndNotify(res, $"원격 URL 설치 완료: {res.Manifest.Name}", saveAsUserPreset: url.EndsWith(".nrig", StringComparison.OrdinalIgnoreCase));
    }

    private PluginImportNrigResDto ApplyImportedRigAndNotify(NrigImportResultDto res, string summary, bool saveAsUserPreset)
    {
        _models = ToneProfileStore.LoadAll();
        _kernel.Processor.SetModelLibrary(_models);

        var chain = res.SignalChain.Clone();
        _settings.ToneMode = (int)ToneMode.SignalChain;
        _settings.SignalChain = chain;

        if (!string.IsNullOrWhiteSpace(res.CustomCabIrPath))
            _settings.CustomCabIrPath = res.CustomCabIrPath;

        if (res.ImportedModels.Count > 0)
        {
            _kernel.Processor.ActiveProfile = res.ImportedModels[0];
            _settings.SelectedToneId = res.ImportedModels[0].Id;
        }

        if (saveAsUserPreset)
        {
            _settings.SavedPresets ??= new List<SignalChainConfigDto>();
            int idx = _settings.SavedPresets.FindIndex(p => string.Equals(p.PresetId, chain.PresetId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _settings.SavedPresets[idx] = chain;
            else _settings.SavedPresets.Add(chain);
        }

        if (res.GuitarProfile != null)
        {
            _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
            int gIdx = _settings.GuitarProfiles.FindIndex(g => string.Equals(g.Id, res.GuitarProfile.Id, StringComparison.OrdinalIgnoreCase));
            if (gIdx >= 0) _settings.GuitarProfiles[gIdx] = res.GuitarProfile;
            else _settings.GuitarProfiles.Insert(0, res.GuitarProfile);
            _settings.ActiveGuitarProfileId = res.GuitarProfile.Id;
        }

        _settings.Save();
        _kernel.LoadChainConfig(chain);
        SyncVisualizerCabIr();
        _wasIdleSettled = false;

        var toneDtos = ToModelDtos(_models);
        string selTone = _kernel.Processor.ActiveProfile?.Id ?? "";
        EmitEvent("tonesChanged", new PluginTonesChangedDto
        {
            Tones = toneDtos,
            SelectedToneId = selTone,
            ToneMode = (int)ToneMode.SignalChain,
        }, PluginJsonContext.Default.PluginTonesChangedDto);

        if (!string.IsNullOrWhiteSpace(res.CustomCabIrName))
        {
            EmitEvent("cabIrChanged", new PluginCabIrChangedDto { CustomCabIrName = res.CustomCabIrName }, PluginJsonContext.Default.PluginCabIrChangedDto);
        }
        if (res.GuitarProfile != null)
        {
            EmitEvent("guitarProfilesChanged", new PluginGuitarProfilesResDto
            {
                Profile = res.GuitarProfile,
                GuitarProfiles = _settings.GuitarProfiles ?? new List<GuitarProfileDto>(),
                ActiveGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
            }, PluginJsonContext.Default.PluginGuitarProfilesResDto);
        }
        EmitEvent("signalChainChanged", new PluginSignalChainChangedDto
        {
            SignalChain = chain,
            SavedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            Summary = summary,
        }, PluginJsonContext.Default.PluginSignalChainChangedDto);

        EmitLog($"[통합 허브] {summary}");
        return new PluginImportNrigResDto
        {
            Cancelled = false,
            SignalChain = chain,
            SavedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            Tones = toneDtos,
            SelectedToneId = selTone,
            CustomCabIrName = res.CustomCabIrName,
            Summary = summary,
        };
    }

    private RigBenchmarkReportDto RunBenchmarkSuite(JsonElement payload)
    {
        int sr = _kernel.EffectiveSampleRate > 0 ? _kernel.EffectiveSampleRate : 48000;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("sampleRate", out var srEl) &&
            srEl.TryGetInt32(out int reqSr) &&
            reqSr is >= 44100 and <= 192000)
        {
            sr = reqSr;
        }
        return RigBenchmarkSuite.Run(sr, null);
    }

    private PluginOkResDto UpdateOnboardingState(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("hasChosenAuthMode", out var authEl))
            {
                if (authEl.ValueKind == JsonValueKind.True) _settings.HasChosenAuthMode = true;
                else if (authEl.ValueKind == JsonValueKind.False) _settings.HasChosenAuthMode = false;
            }
            if (payload.TryGetProperty("hasCompletedTutorial", out var tutEl))
            {
                if (tutEl.ValueKind == JsonValueKind.True) _settings.HasCompletedTutorial = true;
                else if (tutEl.ValueKind == JsonValueKind.False) _settings.HasCompletedTutorial = false;
            }
            if (payload.TryGetProperty("lastGuestLoginPromptDate", out var guestDateEl) && guestDateEl.ValueKind == JsonValueKind.String)
                _settings.LastGuestLoginPromptDate = guestDateEl.GetString()?.Trim();
            if (payload.TryGetProperty("hasShownSponsorPrompt", out var spEl))
            {
                if (spEl.ValueKind == JsonValueKind.True) _settings.HasShownSponsorPrompt = true;
                else if (spEl.ValueKind == JsonValueKind.False) _settings.HasShownSponsorPrompt = false;
            }
            _settings.Save();
        }
        return new PluginOkResDto { Ok = true };
    }

    private static PluginOkResDto OpenExternalUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
            }
            catch { }
        }
        return new PluginOkResDto { Ok = true };
    }

    private static List<PluginModelProfileDto> ToModelDtos(IEnumerable<ToneProfile> profiles)
    {
        return profiles.Select(p => new PluginModelProfileDto(
            p.Id,
            p.Name,
            p.Architecture,
            p.ArchPreset,
            p.SampleRate,
            p.Esr,
            p.EsrDb,
            p.SimilarityPct,
            p.CreatedAt,
            p.NamFilePath,
            p.HasCabIr)).ToList();
    }

    private void EmitLog(string line)
    {
        string clean = LogSanitizer.RecordLog(line);
        EmitEvent("log", new PluginLogEventDto { Line = $"[{DateTime.Now:HH:mm:ss}] {clean}" }, PluginJsonContext.Default.PluginLogEventDto);
    }

    private static JsonElement SerializeEl<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.SerializeToElement(value, typeInfo);

    private void EmitEvent<T>(string type, T payload, JsonTypeInfo<T> typeInfo)
    {
        var ev = new PluginRpcEventDto
        {
            Type = type,
            Data = JsonSerializer.SerializeToElement(payload, typeInfo),
        };
        SendRaw(ev, PluginJsonContext.Default.PluginRpcEventDto);
    }

    private void SendRaw<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        string json = JsonSerializer.Serialize(value, typeInfo);
        _sendJson(json);
    }

    private static string? GetStr(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement el, string prop, int def)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.TryGetInt32(out int n) ? n : def;

    private static float GetFloat(JsonElement el, string prop, float def)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.TryGetSingle(out float n) ? n : def;

    private static bool GetBool(JsonElement el, string prop, bool def)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : def;

    private string? ShowWin32OpenFileDialog(string title, string filter)
        => ShowWin32FileDialog(isSave: false, title, filter, initialFileName: null, defExt: null);

    private string? ShowWin32SaveFileDialog(string title, string filter, string initialFileName, string defExt)
        => ShowWin32FileDialog(isSave: true, title, filter, initialFileName, defExt);

    private string? ShowWin32FileDialog(bool isSave, string title, string filter, string? initialFileName, string? defExt)
    {
        const int MaxChars = 1024;
        nint filterPtr = Marshal.StringToHGlobalUni(filter);
        nint titlePtr = Marshal.StringToHGlobalUni(title);
        nint defExtPtr = string.IsNullOrEmpty(defExt) ? 0 : Marshal.StringToHGlobalUni(defExt);
        nint fileBufPtr = Marshal.AllocHGlobal(MaxChars * 2);
        try
        {
            unsafe
            {
                new Span<byte>((void*)fileBufPtr, MaxChars * 2).Clear();
                if (!string.IsNullOrEmpty(initialFileName))
                {
                    var chars = initialFileName.AsSpan(0, Math.Min(initialFileName.Length, MaxChars - 1));
                    chars.CopyTo(new Span<char>((void*)fileBufPtr, MaxChars));
                }
            }

            var ofn = new OpenFileNameW
            {
                lStructSize = Marshal.SizeOf<OpenFileNameW>(),
                hwndOwner = _parentHwnd,
                lpstrFilter = filterPtr,
                lpstrFile = fileBufPtr,
                nMaxFile = MaxChars,
                lpstrTitle = titlePtr,
                Flags = isSave
                    ? (0x00080000 | 0x00000002 | 0x00000800 | 0x00000008)
                    : (0x00080000 | 0x00001000 | 0x00000800 | 0x00000008),
                lpstrDefExt = defExtPtr,
            };

            bool ok = isSave ? GetSaveFileNameW(ref ofn) : GetOpenFileNameW(ref ofn);
            return ok ? Marshal.PtrToStringUni(fileBufPtr) : null;
        }
        finally
        {
            if (filterPtr != 0) Marshal.FreeHGlobal(filterPtr);
            if (titlePtr != 0) Marshal.FreeHGlobal(titlePtr);
            if (defExtPtr != 0) Marshal.FreeHGlobal(defExtPtr);
            if (fileBufPtr != 0) Marshal.FreeHGlobal(fileBufPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileNameW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileNameW lpofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OpenFileNameW lpofn);

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }
    }
}
