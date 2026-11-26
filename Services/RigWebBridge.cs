using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Plugin;

namespace NeuralRigStudio.Services;

public sealed record PresetDef(int Index, string Name, int RingMs, int SharedMs, int ExclMs);

public sealed record ModelProfileDto(
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

/// <summary>
/// WebView2 JS ↔ 순수 C# 네이티브 실시간 오디오 엔진 브리지.
/// 외부 Python/HTTP 사이드카 없이 모든 COM/ASIO/.nam/IR 명령을 UI STA 스레드에서 직접 처리한다.
/// </summary>
public sealed class RigWebBridge : IDisposable
{
    public const string McpPipeName = "NeuralRigStudio_McpPipe_v1";

    public static readonly PresetDef[] Presets =
    {
        new(0, "극한 (링 4ms / WASAPI 8~12ms)", 4, 12, 8),
        new(1, "최소 (링 8ms / WASAPI 10~15ms)", 8, 15, 10),
        new(2, "균형 (링 16ms / WASAPI 15~25ms)", 16, 25, 15),
        new(3, "안정 (링 32ms / WASAPI 25~50ms)", 32, 50, 25),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Form _owner;
    private readonly AudioRouter _router;
    private readonly VisualizerAnalyzer _visualizer;
    private readonly Action<string> _sendJson;
    private readonly AntigravityToneArchitect _toneArchitect = new();
    private readonly GuitarAnalyzer _guitarAnalyzer = new();
    private readonly MidiRouter _midi = new();
    private readonly CloudPlatformService _cloud = new();
    private readonly float[] _guitarChunk = new float[4096];
    private long _guitarDryCursor = -1;
    private long _lastMidiUiBroadcastTick;
    private readonly CancellationTokenSource _mcpCts = new();

    private AppSettings _settings;
    private List<ToneProfile> _models = new();
    private AsioDevice? _panelDev;
    private bool _audioBusy;
    private float _meterL;
    private float _meterR;
    private float _inMeterL;
    private float _inMeterR;
    private bool _wasIdleSettled;

    public RigWebBridge(
        Form owner,
        AudioRouter router,
        VisualizerAnalyzer visualizer,
        AppSettings settings,
        Action<string> sendJson)
    {
        _owner = owner;
        _router = router;
        _visualizer = visualizer;
        _settings = settings;
        _sendJson = sendJson;

        _router.Log += msg => EmitLog($"[오디오] {msg}");
        _midi.Log += msg => EmitLog(msg);
        _midi.BindingLearned += OnMidiBindingLearned;
        _midi.MidiReceived += OnMidiEventReceived;
        _router.StateChanged += s => EmitEvent("audioState", new { isRunning = _router.IsRunning, busy = _audioBusy, state = s });
        _router.StoppedUnexpectedly += reason =>
        {
            RunOnUi(() =>
            {
                EmitEvent("audioState", new { isRunning = false, busy = false, state = $"예기치 않게 중지됨: {reason}", error = reason });
            });
        };
        _cloud.SessionStateChanged += () => EmitEvent("cloudStateChanged", _cloud.GetState(_settings));

        LoadInitialState();
        _ = Task.Run(() => _cloud.EnsureFreshSessionAsync(_settings, _mcpCts.Token));
        _ = Task.Run(() => RunMcpPipeServerLoopAsync(_mcpCts.Token));
    }

    private void RunOnUi(Action action)
    {
        if (_owner.IsDisposed) return;
        if (_owner.InvokeRequired)
            _owner.BeginInvoke(action);
        else
            action();
    }

    private async Task<T> RunOnUiAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        for (int i = 0; i < 60 && !_owner.IsHandleCreated && !_owner.IsDisposed; i++)
            await Task.Delay(50, ct);
        if (_owner.IsDisposed)
            throw new ObjectDisposedException(nameof(MainForm));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _owner.BeginInvoke(async () =>
        {
            try
            {
                var res = await work();
                tcs.TrySetResult(res);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return await tcs.Task.WaitAsync(ct);
    }

    private void SyncVisualizerCabIr()
    {
        var prof = _router.Tone.ActiveProfile;
        int sr = _router.EffectiveSampleRate > 0 ? _router.EffectiveSampleRate : (prof?.SampleRate ?? 44100);
        _visualizer.SetCabIr(_router.Tone.GetEffectiveCabIr(), sr);
    }

    private void LoadInitialState()
    {
        _models = ToneProfileStore.LoadAll();
        _router.Tone.SetModelLibrary(_models);
        ToneProfile? selected = null;
        if (!string.IsNullOrEmpty(_settings.SelectedToneId))
            selected = _models.FirstOrDefault(t => t.Id == _settings.SelectedToneId);
        selected ??= _models.FirstOrDefault();

        _router.Tone.ActiveProfile = selected;
        if (!string.IsNullOrEmpty(_settings.CustomCabIrPath) && File.Exists(_settings.CustomCabIrPath))
        {
            try
            {
                _router.Tone.LoadCustomCabIr(_settings.CustomCabIrPath);
            }
            catch
            {
                _settings.CustomCabIrPath = null;
            }
        }

        _settings.WhiteBox ??= new WhiteBoxParams();
        _router.Tone.WhiteBox.UpdateParams(_settings.WhiteBox);

        _settings.SignalChain ??= new SignalChainConfigDto();
        _settings.SavedPresets ??= new List<SignalChainConfigDto>();
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        _settings.MidiBindings ??= new List<MidiBindingDto>();
        SpiceModelLibrary.LoadAll();
        foreach (var cSpec in CustomDeviceRegistry.GetAll())
            AntigravityToneArchitect.RegisterCustomDevice(cSpec);
        _router.Tone.Chain.UpdateChain(_settings.SignalChain);

        if (!string.IsNullOrWhiteSpace(_settings.MidiInputName))
            _midi.Connect(_settings.MidiInputName);
        else
        {
            var firstMidi = MidiRouter.GetDeviceNames().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstMidi) && _midi.Connect(firstMidi))
                _settings.MidiInputName = firstMidi;
        }

        int initMode = Math.Clamp(_settings.ToneMode, 0, 6);
        if (selected is null && (initMode == (int)ToneMode.FullRig || initMode == (int)ToneMode.AmpHeadOnly))
            initMode = (int)ToneMode.SignalChain;

        _router.Tone.Mode = (ToneMode)initMode;
        _router.Tone.DriveMultiplier = Math.Clamp(_settings.ToneDrivePct, 20f, 220f) / 100f;
        _router.Tone.WetMix = Math.Clamp(_settings.ToneMixPct, 0f, 100f) / 100f;
        _router.Tone.OutputGainDb = Math.Clamp(_settings.ToneGainDb, -12f, 12f);
        _router.Tone.GateThresholdDb = Math.Clamp(_settings.ToneGateDb, -80f, -20f);
        SyncVisualizerCabIr();

        if (string.IsNullOrEmpty(_settings.InputDriver)
            || _settings.InputDriver.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var drivers = AudioRouter.GetDriverNames();
                var preferred = drivers.FirstOrDefault(IsProAudioInterfaceDriver)
                             ?? drivers.FirstOrDefault(d => !d.Contains("Realtek", StringComparison.OrdinalIgnoreCase));
                if (preferred != null)
                {
                    _settings.InputKind = "ASIO";
                    _settings.InputDriver = preferred;
                    if (_settings.ChannelMode == 0 && IsProAudioInterfaceDriver(preferred))
                        _settings.ChannelMode = 1;
                    _settings.Save();
                }
                else if (drivers.Length == 0)
                {
                    _settings.InputKind = "WASAPI";
                    _settings.Save();
                }
            }
            catch { }
        }
    }

    private static bool IsProAudioInterfaceDriver(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string[] keywords =
        {
            "Focusrite", "Scarlett", "Clarett", "Universal Audio", "Apollo", "Volt",
            "RME", "Fireface", "Babyface", "Audient", "EVO", "MOTU", "Steinberg", "Yamaha",
            "SSL", "Solid State", "PreSonus", "Native Instruments", "Komplete", "Apogee",
            "Antelope", "Roland", "Rubix", "BOSS", "Line 6", "Helix", "Neural",
            "Quad Cortex", "IK Multimedia", "AXE I/O", "iRig", "Kemper", "Fractal", "Fender",
            "Behringer", "UMC", "M-Audio", "Arturia", "MiniFuse", "AudioFuse", "Tascam",
            "Zoom", "Mooer", "NUX", "Hotone", "Ampero", "Valeton", "Rocksmith", "Guitar",
            "ASIO4ALL", "FlexASIO", "USB Audio", "Line In"
        };
        for (int i = 0; i < keywords.Length; i++)
        {
            if (name.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void EmitTelemetryTick()
    {
        var (inL, inR, inClip, l, r, clip) = _router.TakeIoPeaks();
        _inMeterL = Math.Max(inL, _inMeterL * 0.85f);
        _inMeterR = Math.Max(inR, _inMeterR * 0.85f);
        if (_inMeterL < 0.0005f) _inMeterL = 0f;
        if (_inMeterR < 0.0005f) _inMeterR = 0f;

        _meterL = Math.Max(l, _meterL * 0.85f);
        _meterR = Math.Max(r, _meterR * 0.85f);
        if (_meterL < 0.0005f) _meterL = 0f;
        if (_meterR < 0.0005f) _meterR = 0f;

        var backing = _router.GetBackingState();
        var looper = _router.Tone.Looper.GetSnapshot();
        var recorder = _router.Recorder.GetSnapshot();
        bool running = _router.IsRunning;
        bool looperActive = _router.Tone.Looper.State is LooperState.Recording or LooperState.Playing or LooperState.Overdubbing;
        bool settled = !running && !backing.Playing && !_guitarAnalyzer.IsActive && !recorder.Recording && !recorder.ReampPlaying && !looperActive && _meterL == 0f && _meterR == 0f && _inMeterL == 0f && _inMeterR == 0f && !clip && !inClip;
        if (settled && _wasIdleSettled)
            return;
        _wasIdleSettled = settled;

        GuitarAnalysisLiveStateDto? guitarAnalysis = null;
        if (_guitarAnalyzer.IsActive)
        {
            if (running)
            {
                int sr = _router.EffectiveSampleRate > 0 ? _router.EffectiveSampleRate : 44100;
                int got = _router.CopyDrySamplesSince(ref _guitarDryCursor, _guitarChunk);
                if (got > 0)
                    _guitarAnalyzer.FeedDrySamples(_guitarChunk.AsSpan(0, got), sr);
            }
            guitarAnalysis = _guitarAnalyzer.GetLiveState();
        }

        var vis = _visualizer.UpdateFromRouter(_router);
        var lat = _router.GetLatency();

        EmitEvent("telemetry", new
        {
            running,
            busy = _audioBusy,
            directDuplex = _router.IsDirectDuplex,
            meterL = MathF.Round(_meterL, 4),
            meterR = MathF.Round(_meterR, 4),
            clip,
            inMeterL = MathF.Round(_inMeterL, 4),
            inMeterR = MathF.Round(_inMeterR, 4),
            inClip,
            guitarAnalysis,
            latency = lat is null ? null : new
            {
                inMs = Math.Round(lat.Value.InMs, 1),
                ringMs = Math.Round(lat.Value.RingMs, 1),
                outMs = Math.Round(lat.Value.OutMs, 1),
                totalMs = Math.Round(lat.Value.TotalMs, 1),
            },
            counters = new
            {
                padded = _router.PaddedFrames,
                dropped = _router.DroppedFrames,
                lost = _router.LostFrames,
                sampleRate = _router.EffectiveSampleRate,
            },
            backing,
            looper,
            recorder,
            vis = new
            {
                running = vis.Running,
                hasSignal = vis.HasSignal,
                hasIrCurve = vis.HasIrCurve,
                peakDb = vis.PeakDb,
                bands = vis.Bands,
                peakCaps = vis.PeakCaps,
                wave = vis.Wave,
                irCurve = vis.IrCurve,
                tuner = vis.Tuner,
            }
        });
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

            object? data = action switch
            {
                "init" => BuildInitSnapshot(),
                "refreshAudioDevices" => BuildAudioDevicesSnapshot(),
                "getAsioCaps" => GetAsioCapabilities(GetStr(payload, "driver")),
                "openAsioPanel" => OpenAsioPanel(GetStr(payload, "driver")),
                "startMonitoring" => await StartMonitoringAsync(payload),
                "stopMonitoring" => StopMonitoring(),
                "updateToneParams" => UpdateToneParams(payload),
                "updateWhiteBoxParams" => UpdateWhiteBoxParams(payload),
                "updateSignalChain" => UpdateSignalChain(payload),
                "setIoGain" => SetIoGain(payload),
                "saveUserPreset" => SaveUserPreset(payload),
                "deleteUserPreset" => DeleteUserPreset(GetStr(payload, "presetId")),
                "selectTone" => SelectTone(GetStr(payload, "id")),
                "deleteTone" => DeleteTone(GetStr(payload, "id")),
                "importNamFile" => ImportNamFile(payload),
                "exportNamFile" => ExportNamFile(payload),
                "loadCustomCabIr" => LoadCustomCabIr(payload),
                "clearCustomCabIr" => ClearCustomCabIr(),
                "loadBackingTrack" => LoadBackingTrack(payload),
                "controlBackingTrack" => ControlBackingTrack(payload),
                "unloadBackingTrack" => UnloadBackingTrack(),
                "startGuitarAnalysis" => StartGuitarAnalysis(payload),
                "cancelGuitarAnalysis" => CancelGuitarAnalysis(),
                "skipGuitarAnalysisStep" => SkipGuitarAnalysisStep(),
                "saveGuitarProfile" => SaveGuitarProfile(payload),
                "selectGuitarProfile" => SelectGuitarProfile(GetStr(payload, "id")),
                "deleteGuitarProfile" => DeleteGuitarProfile(GetStr(payload, "id")),
                "applyGuitarCalibration" => ApplyGuitarCalibration(GetStr(payload, "id")),
                "setTunerConfig" => SetTunerConfig(payload),
                "generateAiTone" => GenerateAiToneFromUi(payload),
                "refreshMidiDevices" => BuildMidiSnapshot(),
                "selectMidiDevice" => SelectMidiDevice(GetStr(payload, "deviceName")),
                "startMidiLearn" => StartMidiLearn(payload),
                "cancelMidiLearn" => CancelMidiLearn(),
                "clearMidiBinding" => ClearMidiBinding(payload),
                "looperCommand" => HandleLooperCommand(payload),
                "recorderCommand" => HandleRecorderCommand(payload),
                "exportNrigBundle" => ExportNrigBundle(payload),
                "importNrigBundle" => ImportNrigBundle(payload),
                "importDroppedFile" => ImportDroppedFile(payload),
                "getToneHubCatalog" => GetToneHubCatalog(),
                "installToneHubItem" => InstallToneHubItem(payload),
                "installToneHubUrl" => await InstallToneHubUrlAsync(payload),
                "exportDawPlugin" => ExportDawPlugin(payload),
                "setWindowTheme" => SetWindowTheme(payload),
                "runBenchmarkSuite" => await Task.Run(() => RunBenchmarkSuite(payload)),
                "updateOnboardingState" => UpdateOnboardingState(payload),
                "openExternalUrl" => OpenExternalUrl(GetStr(payload, "url")),
                "cloudSignInGoogle" => await SignInGoogleAndNotifyAsync(),
                "cloudContinueGuest" => ContinueGuestAndNotify(),
                "cloudSignOut" => SignOutAndNotify(),
                "cloudSyncNow" => await CloudSyncNowAsync(),
                "getCommunityHub" => await GetCommunityHubAsync(payload),
                "uploadCommunityGear" => await UploadCommunityGearAsync(payload),
                "installCommunityGear" => await InstallCommunityGearAsync(payload),
                "submitBugReport" => await SubmitBugReportAsync(payload),
                "getSanitizedLogsPreview" => new
                {
                    logs = LogSanitizer.GetRecentLogsText(250),
                    pendingCount = _cloud.GetState(_settings).PendingBugReportsCount,
                },
                "checkOtaUpdate" => await OtaUpdateService.CheckForUpdateAsync(CloudPlatformService.ResolveCloudBaseUrl(_settings)),
                "applyOtaUpdate" => await ApplyOtaUpdateAsync(payload),
                "getAdminOverview" => await _cloud.GetAdminOverviewAsync(_settings),
                "adminUpdateBugStatus" => await _cloud.UpdateAdminBugStatusAsync(_settings, GetStr(payload, "id") ?? "", GetStr(payload, "status") ?? "triaged"),
                "adminUpdateReleasePolicy" => await _cloud.UpdateAdminReleasePolicyAsync(_settings, payload),
                _ => throw new InvalidOperationException($"알 수 없는 액션: {action}"),
            };

            if (reqId != null)
                EmitRaw(new { type = "response", reqId, ok = true, data });
        }
        catch (Exception ex)
        {
            if (reqId != null)
                EmitRaw(new { type = "response", reqId, ok = false, error = ex.Message });
            else
                EmitLog($"[브리지 오류] {ex.Message}");
        }
    }

    private object BuildInitSnapshot()
    {
        _cloud.EnsureFirstSignInTracked(_settings);
        var audio = BuildAudioDevicesSnapshot();
        var active = _router.Tone.ActiveProfile;
        return new
        {
            settings = _settings,
            presets = Presets,
            audio,
            tones = ToModelDtos(_models),
            selectedToneId = active?.Id ?? "",
            customCabIrName = _router.Tone.CustomCabIrName,
            whiteBox = _router.Tone.WhiteBox.GetParamsSnapshot(),
            signalChain = _router.Tone.Chain.GetConfigSnapshot(),
            ioGainLock = _settings.IoGainLock,
            savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            guitarProfiles = _settings.GuitarProfiles ?? new List<GuitarProfileDto>(),
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
            tunerMute = _router.TunerMute,
            tunerRefA4Hz = _visualizer.TunerRefA4Hz,
            backing = _router.GetBackingState(),
            looper = _router.Tone.Looper.GetSnapshot(),
            recorder = _router.Recorder.GetSnapshot(),
            midi = BuildMidiSnapshot(),
            customDevices = CustomDeviceRegistry.GetAll(),
            isAudioRunning = _router.IsRunning,
            appVersion = OtaUpdateService.CurrentVersion,
            cloudState = _cloud.GetState(_settings),
        };
    }

    private object BuildAudioDevicesSnapshot()
    {
        var sortedDrivers = AudioRouter.GetDriverNames()
            .OrderByDescending(IsProAudioInterfaceDriver)
            .ThenByDescending(d => !d.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var wasapi = new List<object> { new { id = "", label = "시스템 기본 출력 장치" } };
        var wasapiInputs = new List<object> { new { id = "", label = "시스템 기본 입력 장치 (자동)" } };
        try
        {
            var en = new MMDeviceEnumerator();
            string defaultRenderId = "";
            try { defaultRenderId = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console).ID; } catch { }
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                string label = d.FriendlyName + (d.ID == defaultRenderId ? " (기본)" : "");
                wasapi.Add(new { id = d.ID, label });
            }

            string defaultCapId = "";
            try { defaultCapId = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID; } catch { }
            var capEndpoints = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .OrderByDescending(d => IsProAudioInterfaceDriver(d.FriendlyName))
                .ThenByDescending(d => d.ID == defaultCapId)
                .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase);
            foreach (var d in capEndpoints)
            {
                int ch = 2;
                int sr = 48000;
                try
                {
                    using var ac = d.CreateAudioClient();
                    var mix = ac.MixFormat;
                    ch = mix.Channels;
                    sr = mix.SampleRate;
                }
                catch { }
                string suffix = d.ID == defaultCapId ? $" (기본 · {ch}ch {sr / 1000.0:0.#}kHz)" : $" ({ch}ch {sr / 1000.0:0.#}kHz)";
                wasapiInputs.Add(new { id = d.ID, label = d.FriendlyName + suffix, channels = ch, sampleRate = sr });
            }
        }
        catch (Exception ex)
        {
            EmitLog($"WASAPI 목록 조회 실패: {ex.Message}");
        }

        string? initialDriver = !string.IsNullOrEmpty(_settings.InputDriver) && sortedDrivers.Contains(_settings.InputDriver)
            ? (_settings.InputDriver.Contains("Realtek", StringComparison.OrdinalIgnoreCase) && sortedDrivers.Any(IsProAudioInterfaceDriver)
                ? sortedDrivers.First(IsProAudioInterfaceDriver)
                : _settings.InputDriver)
            : sortedDrivers.FirstOrDefault();

        object? initialCaps = !string.IsNullOrEmpty(initialDriver) && !_router.IsRunning
            ? GetAsioCapabilities(initialDriver)
            : null;

        return new
        {
            asioDrivers = sortedDrivers,
            wasapiDevices = wasapi,
            wasapiInputDevices = wasapiInputs,
            initialDriver,
            initialCaps,
        };
    }

    private object GetAsioCapabilities(string? driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
            return new { driverName = "", sampleRate = 44100, outputChannelsCount = 0, inputChannels = Array.Empty<string>(), bufferSizes = new[] { 256, 0 } };

        if (_router.IsRunning)
        {
            int sr = _router.EffectiveSampleRate > 0 ? _router.EffectiveSampleRate : 44100;
            return new { driverName, sampleRate = sr, outputChannelsCount = 2, inputChannels = new[] { "In 1", "In 2" }, bufferSizes = new[] { 128, 256, 512, 0 } };
        }

        try
        {
            using var probe = AsioDevice.Open(driverName);
            double rate = probe.CurrentSampleRate > 0 ? probe.CurrentSampleRate : 44100;
            var caps = probe.Capabilities;
            var chNames = new List<string>();
            if (caps.InputChannelInfos != null)
            {
                for (int i = 0; i < Math.Min(caps.NbInputChannels, caps.InputChannelInfos.Length); i++)
                    chNames.Add(caps.InputChannelInfos[i].name);
            }
            var sizes = AudioRouter.ValidBufferSizes(caps).OrderBy(x => x).ToList();
            sizes.Add(0);
            return new
            {
                driverName,
                sampleRate = rate,
                preferred = caps.BufferPreferredSize,
                outputChannelsCount = caps.NbOutputChannels,
                inputChannels = chNames,
                bufferSizes = sizes,
            };
        }
        catch (Exception ex)
        {
            EmitLog($"[프로브] '{driverName}' 조회 실패: {ex.Message}");
            return new { driverName, sampleRate = 44100, outputChannelsCount = 0, inputChannels = Array.Empty<string>(), bufferSizes = new[] { 0 } };
        }
    }

    private object OpenAsioPanel(string? driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName))
            throw new InvalidOperationException("선택된 ASIO 드라이버가 없습니다.");
        if (_router.IsRunning)
            throw new InvalidOperationException("모니터링을 중지한 후 ASIO 제어판을 여세요.");
        _panelDev?.Dispose();
        _panelDev = null;
        var dev = AsioDevice.Open(driverName);
        _panelDev = dev;
        dev.ShowControlPanel();
        return new { opened = true };
    }

    private async Task<object> StartMonitoringAsync(JsonElement payload)
    {
        if (_audioBusy) throw new InvalidOperationException("오디오 초기화가 이미 진행 중입니다.");
        if (_router.IsRunning) return new { isRunning = true };

        _panelDev?.Dispose();
        _panelDev = null;

        string reqInputKind = (GetStr(payload, "inputKind") ?? _settings.InputKind ?? "ASIO").Trim().ToUpperInvariant();
        bool useWasapiIn = reqInputKind == "WASAPI";

        string? inputDriver = GetStr(payload, "inputDriver") ?? _settings.InputDriver;
        string? wasapiInId = GetStr(payload, "wasapiInputDeviceId") ?? _settings.WasapiInputDeviceId;
        if (string.IsNullOrWhiteSpace(wasapiInId)) wasapiInId = null;
        bool wasapiInExcl = GetBool(payload, "wasapiInputExclusive", _settings.WasapiInputExclusive);
        bool wasapiInRaw = GetBool(payload, "wasapiInputRaw", _settings.WasapiInputRaw);

        if (!useWasapiIn)
        {
            var allAsio = AudioRouter.GetDriverNames();
            if (string.IsNullOrWhiteSpace(inputDriver)
                || inputDriver.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            {
                var preferred = allAsio.FirstOrDefault(IsProAudioInterfaceDriver)
                             ?? allAsio.FirstOrDefault(d => !d.Contains("Realtek", StringComparison.OrdinalIgnoreCase));
                if (preferred != null)
                {
                    if (!string.Equals(inputDriver, preferred, StringComparison.OrdinalIgnoreCase))
                        EmitLog($"[오디오] 외부 인터페이스 감지됨: '{preferred}' 자동 선택");
                    inputDriver = preferred;
                }
                else if (string.IsNullOrWhiteSpace(inputDriver))
                {
                    inputDriver = allAsio.FirstOrDefault();
                }
            }

            if (string.IsNullOrWhiteSpace(inputDriver))
            {
                EmitLog("[범용 입력] 설치된 ASIO 드라이버가 없어 범용 Windows 입력(WASAPI) 모드로 전환합니다.");
                useWasapiIn = true;
                reqInputKind = "WASAPI";
            }
        }

        int channelMode = GetInt(payload, "channelMode", _settings.ChannelMode);
        int channelOffset = Math.Max(0, GetInt(payload, "inputChannelOffset", _settings.InputChannelOffset));
        int bufSize = GetInt(payload, "asioInBufferSize", _settings.AsioInBufferSize);
        string outputKind = GetStr(payload, "outputKind") ?? _settings.OutputKind ?? "WASAPI";
        if (useWasapiIn && string.Equals(outputKind, "ASIO_DUPLEX", StringComparison.OrdinalIgnoreCase))
            outputKind = "WASAPI";

        bool exclusive = GetBool(payload, "wasapiExclusive", _settings.WasapiExclusive);
        string? wasapiId = GetStr(payload, "wasapiDeviceId") ?? _settings.WasapiDeviceId;
        if (string.IsNullOrWhiteSpace(wasapiId)) wasapiId = null;
        string? asioOutDriver = GetStr(payload, "asioOutDriver") ?? _settings.AsioOutDriver;
        int presetIdx = Math.Clamp(GetInt(payload, "presetIndex", _settings.PresetIndex), 0, Presets.Length - 1);
        var preset = Presets[presetIdx];

        _settings.InputKind = useWasapiIn ? "WASAPI" : "ASIO";
        if (!string.IsNullOrWhiteSpace(inputDriver)) _settings.InputDriver = inputDriver;
        _settings.WasapiInputDeviceId = wasapiInId;
        _settings.WasapiInputExclusive = wasapiInExcl;
        _settings.WasapiInputRaw = wasapiInRaw;
        _settings.ChannelMode = channelMode;
        _settings.InputChannelOffset = channelOffset;
        _settings.AsioInBufferSize = bufSize;
        _settings.OutputKind = outputKind;
        _settings.WasapiExclusive = exclusive;
        _settings.WasapiDeviceId = wasapiId;
        _settings.AsioOutDriver = asioOutDriver;
        _settings.PresetIndex = presetIdx;

        OutputKind kind = string.Equals(outputKind, "ASIO_DUPLEX", StringComparison.OrdinalIgnoreCase)
            ? OutputKind.AsioDirectDuplex
            : string.Equals(outputKind, "ASIO", StringComparison.OrdinalIgnoreCase)
                ? OutputKind.AsioOut
                : OutputKind.Wasapi;
        bool isAnyAsioOut = kind != OutputKind.Wasapi;

        var cfg = new RouterConfig
        {
            InputKind = useWasapiIn ? InputBackendKind.Wasapi : InputBackendKind.Asio,
            InputDriver = inputDriver ?? "",
            WasapiInDeviceId = wasapiInId,
            WasapiInExclusive = wasapiInExcl,
            WasapiInRaw = wasapiInRaw,
            ChannelMode = channelMode,
            InputChannelOffset = channelOffset,
            AsioInBufferSize = bufSize,
            Kind = kind,
            Exclusive = exclusive,
            WasapiDeviceId = wasapiId,
            AsioOutDriver = asioOutDriver,
            RingTargetMs = kind == OutputKind.AsioDirectDuplex ? 0 : preset.RingMs,
            WasapiLatencyMs = isAnyAsioOut && !useWasapiIn ? 0 : (exclusive ? preset.ExclMs : preset.SharedMs),
        };

        _audioBusy = true;
        EmitEvent("audioState", new { isRunning = false, busy = true, state = "오디오 엔진 초기화 중…" });
        try
        {
            try
            {
                await _router.StartAsync(cfg);
            }
            catch (Exception ex) when (!useWasapiIn && (inputDriver ?? "").Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            {
                var altAsio = AudioRouter.GetDriverNames().FirstOrDefault(IsProAudioInterfaceDriver)
                           ?? AudioRouter.GetDriverNames().FirstOrDefault(d => !d.Contains("Realtek", StringComparison.OrdinalIgnoreCase));
                if (altAsio != null)
                {
                    EmitLog($"[자동 전환] '{inputDriver}' 실패({ex.Message}) → '{altAsio}' ASIO 드라이버로 재시도");
                    inputDriver = altAsio;
                    cfg.InputDriver = altAsio;
                    _settings.InputDriver = altAsio;
                    await _router.StartAsync(cfg);
                }
                else
                {
                    EmitLog($"[범용 폴백] '{inputDriver}' ASIO 시작 실패({ex.Message}) → 범용 Windows 입력(WASAPI) 모드로 자동 전환");
                    useWasapiIn = true;
                    cfg.InputKind = InputBackendKind.Wasapi;
                    cfg.Kind = kind == OutputKind.AsioDirectDuplex ? OutputKind.Wasapi : kind;
                    cfg.RingTargetMs = preset.RingMs;
                    cfg.WasapiLatencyMs = exclusive ? preset.ExclMs : preset.SharedMs;
                    _settings.InputKind = "WASAPI";
                    if (cfg.Kind == OutputKind.Wasapi) _settings.OutputKind = "WASAPI";
                    await _router.StartAsync(cfg);
                }
            }

            _settings.Save();
            SyncVisualizerCabIr();
            string okText = useWasapiIn
                ? "실시간 범용 입력(WASAPI) 모니터링 중"
                : cfg.Kind == OutputKind.AsioDirectDuplex
                    ? "ASIO 직결 풀듀플렉스 초저지연 모니터링 중"
                    : "실시간 ASIO 저지연 모니터링 중";
            EmitEvent("audioState", new { isRunning = true, busy = false, state = okText });
            return new
            {
                isRunning = true,
                inputKind = useWasapiIn ? "WASAPI" : "ASIO",
                inputDriver = inputDriver ?? "",
                outputKind = _settings.OutputKind,
            };
        }
        catch (Exception ex)
        {
            EmitEvent("audioState", new { isRunning = false, busy = false, state = "시작 실패" });
            if (!useWasapiIn)
                throw new InvalidOperationException($"ASIO 오디오 인터페이스 ({inputDriver}) 연결 실패: 전원 및 USB 케이블·샘플레이트 잠금을 확인하거나 'WASAPI / LINE-IN' 탭으로 전환해 보세요. ({ex.Message})", ex);
            throw;
        }
        finally
        {
            _audioBusy = false;
        }
    }

    private object StopMonitoring()
    {
        _router.Stop("사용자 정지");
        EmitEvent("audioState", new { isRunning = false, busy = false, state = "중지됨" });
        return new { isRunning = false };
    }

    private object UpdateToneParams(JsonElement payload)
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

        var effectiveMode = (ToneMode)mode;
        if (_router.Tone.ActiveProfile is null && (effectiveMode == ToneMode.FullRig || effectiveMode == ToneMode.AmpHeadOnly))
            effectiveMode = ToneMode.SignalChain;

        _router.Tone.Mode = effectiveMode;
        _router.Tone.DriveMultiplier = drivePct / 100f;
        _router.Tone.WetMix = mixPct / 100f;
        _router.Tone.OutputGainDb = gainDb;
        _router.Tone.GateThresholdDb = gateDb;
        SyncVisualizerCabIr();
        return new { ok = true, toneMode = (int)effectiveMode };
    }

    private object SetIoGain(JsonElement payload)
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

        _router.Tone.Chain.SetIoGainFromUi(inDb, outDb);
        _settings.SignalChain ??= new SignalChainConfigDto();
        if (inDb.HasValue) _settings.SignalChain.InputGainDb = inDb.Value;
        if (outDb.HasValue) _settings.SignalChain.OutputGainDb = outDb.Value;
        if (commit || lockChanged)
            _settings.Save();

        return new
        {
            ok = true,
            inputGainDb = _settings.SignalChain.InputGainDb,
            outputGainDb = _settings.SignalChain.OutputGainDb,
            ioGainLock = _settings.IoGainLock,
        };
    }

    private object UpdateSignalChain(JsonElement payload)
    {
        SignalChainConfigDto? cfg = null;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            cfg = JsonSerializer.Deserialize<SignalChainConfigDto>(payload.GetRawText(), JsonOpts);
        }
        cfg ??= new SignalChainConfigDto();
        var clean = cfg.Clone();
        _settings.SignalChain = clean;
        _settings.ToneMode = (int)ToneMode.SignalChain;
        _settings.Save();

        _router.Tone.Chain.UpdateChain(clean);
        _router.Tone.Mode = ToneMode.SignalChain;
        SyncVisualizerCabIr();
        return new { ok = true, signalChain = _router.Tone.Chain.GetConfigSnapshot() };
    }

    private object SaveUserPreset(JsonElement payload)
    {
        SignalChainConfigDto? cfg = null;
        if (payload.ValueKind == JsonValueKind.Object)
            cfg = JsonSerializer.Deserialize<SignalChainConfigDto>(payload.GetRawText(), JsonOpts);
        cfg ??= _router.Tone.Chain.GetConfigSnapshot();
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
        return new { ok = true, savedPresets = _settings.SavedPresets };
    }

    private object DeleteUserPreset(string? presetId)
    {
        _settings.SavedPresets ??= new List<SignalChainConfigDto>();
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            _settings.SavedPresets.RemoveAll(p => string.Equals(p.PresetId, presetId, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
        }
        return new { ok = true, savedPresets = _settings.SavedPresets };
    }

    private async Task RunMcpPipeServerLoopAsync(CancellationToken ct)
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    McpPipeName,
                    PipeDirection.InOut,
                    4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
                using var writer = new StreamWriter(server, utf8NoBom, bufferSize: 8192, leaveOpen: true) { AutoFlush = true };

                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string responseJson = await HandleMcpIpcLineAsync(line, ct).ConfigureAwait(false);
                    await writer.WriteLineAsync(responseJson.AsMemory(), ct).ConfigureAwait(false);
                    await writer.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                EmitLog($"[MCP IPC] 파이프 처리 경고: {ex.Message}");
                try { await Task.Delay(150, ct).ConfigureAwait(false); } catch { break; }
            }
            finally
            {
                try { server?.Dispose(); } catch { }
            }
        }
    }

    private async Task<string> HandleMcpIpcLineAsync(string rawLine, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawLine);
            var root = doc.RootElement;
            string cmd = GetStr(root, "cmd") ?? "";
            JsonElement args = root.TryGetProperty("args", out var aEl) ? aEl.Clone() : default;

            object result = await RunOnUiAsync(async () => cmd switch
            {
                "get_state" => BuildMcpStateSnapshot(),
                "get_guitar_profiles" => BuildMcpGuitarProfilesSnapshot(),
                "generate_tone" => ExecuteMcpGenerateTone(args),
                "set_preset" => ExecuteMcpSetPreset(args),
                "update_slot" => ExecuteMcpUpdateSlot(args),
                "switch_scene" => ExecuteMcpSwitchScene(args),
                "register_spice_model" => ExecuteMcpRegisterSpiceModel(args),
                "commit_spice_device" => ExecuteMcpCommitSpiceDevice(args),
                "delete_custom_device" => ExecuteMcpDeleteCustomDevice(args),
                "control_audio" => await ExecuteMcpControlAudioAsync(args),
                _ => throw new InvalidOperationException($"알 수 없는 MCP IPC 명령: {cmd}"),
            }, ct).ConfigureAwait(false);

            return JsonSerializer.Serialize(new { ok = true, data = result }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOpts);
        }
    }

    private GuitarProfileDto? GetActiveGuitarProfile()
    {
        var list = _settings.GuitarProfiles ?? new List<GuitarProfileDto>();
        if (list.Count == 0 || string.IsNullOrWhiteSpace(_settings.ActiveGuitarProfileId))
            return null;
        return list.FirstOrDefault(g => string.Equals(g.Id, _settings.ActiveGuitarProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private object BuildMcpStateSnapshot()
    {
        var activeGuitar = GetActiveGuitarProfile();
        return new
        {
            liveConnected = true,
            isAudioRunning = _router.IsRunning,
            inputDriver = _settings.InputDriver ?? "",
            channelMode = _settings.ChannelMode,
            outputKind = _settings.OutputKind,
            sampleRate = _router.EffectiveSampleRate,
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
            activeGuitarProfile = activeGuitar,
            guitarProfilesCount = _settings.GuitarProfiles?.Count ?? 0,
            customDevices = CustomDeviceRegistry.GetAll(),
            signalChain = _router.Tone.Chain.GetConfigSnapshot(),
            savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
        };
    }

    private object BuildMcpGuitarProfilesSnapshot() => new
    {
        liveConnected = true,
        activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        activeGuitarProfile = GetActiveGuitarProfile(),
        guitarProfiles = _settings.GuitarProfiles ?? new List<GuitarProfileDto>(),
    };

    private object ExecuteMcpGenerateTone(JsonElement args)
    {
        string prompt = GetStr(args, "prompt") ?? "";
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("prompt 파라미터가 필요합니다.");
        bool autoSave = GetBool(args, "autoSave", true);

        var curChain = _router.Tone.Chain.GetConfigSnapshot();
        var activeGuitar = GetActiveGuitarProfile();
        var res = _toneArchitect.GenerateViaBuiltinArchitect(prompt, curChain, activeGuitar);
        ApplyAndBroadcastMcpChain(res.Chain, autoSave, $"{res.PresetName} — {res.Summary}");
        return new { result = res, savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>() };
    }

    private object ExecuteMcpSetPreset(JsonElement args)
    {
        bool autoSave = GetBool(args, "autoSave", true);
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("slots", out _))
            throw new InvalidOperationException("유효한 프리셋 JSON 구성(slots 배열)이 필요합니다.");

        var norm = AntigravityToneArchitect.ParsePresetFromJson(args);
        ApplyAndBroadcastMcpChain(norm, autoSave, $"Antigravity MCP 프리셋 적용: {norm.PresetName}");
        return new { presetId = norm.PresetId, presetName = norm.PresetName, signalChain = norm, savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>() };
    }

    private object ExecuteMcpUpdateSlot(JsonElement args)
    {
        int slotIndex = GetInt(args, "slotIndex", -1);
        if (slotIndex < 0 || slotIndex >= SignalChainConfigDto.SlotCount)
            throw new InvalidOperationException("slotIndex는 0..15 범위여야 합니다.");

        string? modelId = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("modelId", out var mEl) && mEl.ValueKind == JsonValueKind.String
            ? mEl.GetString()
            : null;
        bool? enabled = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("enabled", out var eEl) && (eEl.ValueKind == JsonValueKind.True || eEl.ValueKind == JsonValueKind.False)
            ? eEl.GetBoolean()
            : null;
        int? syncDiv = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("syncDivision", out var sdEl) && sdEl.TryGetInt32(out int sdi)
            ? sdi
            : null;
        JsonElement paramsEl = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("params", out var pEl) ? pEl : default;

        var cur = _router.Tone.Chain.GetConfigSnapshot();
        var updated = AntigravityToneArchitect.ApplySlotEdit(cur, slotIndex, modelId, enabled, paramsEl, syncDiv);
        ApplyAndBroadcastMcpChain(updated, autoSave: false, $"슬롯 #{slotIndex + 1} 실시간 수정 완료");
        return new { slotIndex, signalChain = updated };
    }

    private object ExecuteMcpSwitchScene(JsonElement args)
    {
        int? sceneIdx = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("sceneIndex", out var sEl) && sEl.TryGetInt32(out int si)
            ? si
            : null;
        int? routingMode = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("routingMode", out var rEl) && rEl.TryGetInt32(out int ri)
            ? ri
            : null;
        float? parallelMix = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("parallelMix", out var pEl) && pEl.TryGetSingle(out float pm)
            ? pm
            : null;
        float? tempoBpm = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("tempoBpm", out var tEl) && tEl.TryGetSingle(out float tb)
            ? tb
            : null;

        var cur = _router.Tone.Chain.GetConfigSnapshot();
        var updated = AntigravityToneArchitect.SwitchScene(cur, sceneIdx, routingMode, parallelMix, tempoBpm);
        ApplyAndBroadcastMcpChain(updated, autoSave: false, $"Scene {((char)('A' + updated.ActiveScene))} / {updated.TempoBpm:F0} BPM 전환");
        return new { activeScene = updated.ActiveScene, routingMode = updated.RoutingMode, parallelMix = updated.ParallelMix, tempoBpm = updated.TempoBpm, signalChain = updated };
    }

    private object ExecuteMcpRegisterSpiceModel(JsonElement args)
    {
        var model = JsonSerializer.Deserialize<SpiceComponentModelDto>(args.GetRawText(), JsonOpts)
            ?? throw new InvalidOperationException("유효한 SpiceComponentModelDto JSON이 필요합니다.");
        var registered = SpiceModelLibrary.RegisterOrUpdate(model);
        EmitLog($"[MCP SPICE 모델 등록] {registered.Name} ({registered.Kind}) · 출처: {registered.DatasheetUrl}");
        return new { ok = true, model = registered };
    }

    private object ExecuteMcpCommitSpiceDevice(JsonElement args)
    {
        SpiceModelLibrary.ReloadFromDisk();
        JsonElement devEl = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("device", out var dEl)
            ? dEl : args;
        var spec = JsonSerializer.Deserialize<CustomDeviceSpecDto>(devEl.GetRawText(), JsonOpts)
            ?? throw new InvalidOperationException("유효한 CustomDeviceSpecDto JSON이 필요합니다.");

        int? slotIndex = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("slotIndex", out var sEl) && sEl.TryGetInt32(out int si)
            ? si : null;

        if (string.Equals(spec.Category, "cab", StringComparison.OrdinalIgnoreCase))
        {
            var cabRes = SpiceAnalyzer.AnalyzeCabinet(spec.CabAcoustic);
            if (!cabRes.Ok)
                throw new InvalidOperationException(cabRes.Summary);
        }
        else
        {
            var simRes = SpiceAnalyzer.AnalyzeCircuit(spec.SpiceCircuit ?? new SpiceCircuitDto());
            if (!simRes.Ok)
                throw new InvalidOperationException(simRes.Summary);
        }

        var registered = CustomDeviceRegistry.RegisterOrUpdate(spec);
        EmitEvent("customDevicesChanged", new
        {
            customDevices = CustomDeviceRegistry.GetAll(),
            addedDevice = registered,
        });
        EmitLog($"[MCP SPICE 장비 추가] {registered.Name} — {registered.SchematicSummary}");

        SignalChainConfigDto? chainOut = null;
        if (slotIndex is >= 0 and < SignalChainConfigDto.SlotCount)
        {
            var cur = _router.Tone.Chain.GetConfigSnapshot();
            chainOut = AntigravityToneArchitect.ApplySlotEdit(cur, slotIndex.Value, registered.Id, enabled: true, paramsEl: default);
            ApplyAndBroadcastMcpChain(chainOut, autoSave: false, $"SPICE 커스텀 장비 '{registered.Name}' 슬롯 #{slotIndex.Value + 1} 배치");
        }
        else
        {
            // Refresh live DSP engine in case an active slot updated the same device ID
            _router.Tone.Chain.UpdateChain(_router.Tone.Chain.GetConfigSnapshot());
        }

        return new
        {
            ok = true,
            device = registered,
            assignedSlotIndex = slotIndex,
            signalChain = chainOut,
        };
    }

    private object ExecuteMcpDeleteCustomDevice(JsonElement args)
    {
        string modelId = GetStr(args, "modelId") ?? "";
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("modelId 파라미터가 필요합니다.");

        bool removed = CustomDeviceRegistry.Remove(modelId);
        if (removed)
        {
            EmitEvent("customDevicesChanged", new
            {
                customDevices = CustomDeviceRegistry.GetAll(),
            });
            EmitLog($"[MCP 장비 삭제] {modelId} 삭제됨");
        }
        return new { ok = removed, modelId };
    }

    private async Task<object> ExecuteMcpControlAudioAsync(JsonElement args)
    {
        string action = (GetStr(args, "action") ?? "status").Trim().ToLowerInvariant();
        if (action == "stop")
            return StopMonitoring();
        if (action is "start" or "start_wasapi" or "start_asio_wasapi" or "start_asio_duplex")
        {
            if (_router.IsRunning)
                return new { isRunning = true, inputKind = _settings.InputKind, inputDriver = _settings.InputDriver ?? "" };

            bool forceAsio = action is "start_asio_wasapi" or "start_asio_duplex";
            bool forceWasapiIn = action == "start_wasapi";
            var proDriver = AudioRouter.GetDriverNames().FirstOrDefault(IsProAudioInterfaceDriver);
            var payloadObj = new
            {
                inputKind = forceWasapiIn ? "WASAPI" : forceAsio ? "ASIO" : _settings.InputKind,
                inputDriver = forceAsio ? (proDriver ?? _settings.InputDriver ?? "") : (_settings.InputDriver ?? proDriver ?? ""),
                wasapiInputDeviceId = _settings.WasapiInputDeviceId ?? "",
                wasapiInputExclusive = _settings.WasapiInputExclusive,
                wasapiInputRaw = _settings.WasapiInputRaw,
                channelMode = _settings.ChannelMode,
                inputChannelOffset = _settings.InputChannelOffset,
                asioInBufferSize = _settings.AsioInBufferSize,
                outputKind = action == "start_asio_duplex" ? "ASIO_DUPLEX" : action == "start_asio_wasapi" || forceWasapiIn ? "WASAPI" : _settings.OutputKind,
                wasapiExclusive = _settings.WasapiExclusive,
                wasapiDeviceId = _settings.WasapiDeviceId ?? "",
                asioOutDriver = _settings.AsioOutDriver ?? "",
                presetIndex = _settings.PresetIndex,
            };
            using var pDoc = JsonDocument.Parse(JsonSerializer.Serialize(payloadObj, JsonOpts));
            return await StartMonitoringAsync(pDoc.RootElement);
        }
        return BuildMcpStateSnapshot();
    }

    private void ApplyAndBroadcastMcpChain(SignalChainConfigDto chain, bool autoSave, string summary, string logTag = "MCP")
    {
        var clean = chain.Clone();
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

        _router.Tone.Chain.UpdateChain(clean);
        _router.Tone.Mode = ToneMode.SignalChain;
        SyncVisualizerCabIr();

        EmitEvent("signalChainChanged", new
        {
            signalChain = clean,
            savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            summary,
        });
        EmitLog($"[{logTag}] {summary}");
    }

    private object UpdateWhiteBoxParams(JsonElement payload)
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
        _router.Tone.WhiteBox.UpdateParams(wb);
        return new { ok = true, whiteBox = _router.Tone.WhiteBox.GetParamsSnapshot() };
    }

    private object SelectTone(string? id)
    {
        var prof = string.IsNullOrWhiteSpace(id) ? null : _models.FirstOrDefault(t => t.Id == id);
        _router.Tone.ActiveProfile = prof;
        int nextMode = Math.Clamp(_settings.ToneMode, 0, 6);
        if (nextMode != (int)ToneMode.SignalChain)
        {
            if (prof != null && (nextMode == (int)ToneMode.Bypass || nextMode == (int)ToneMode.WhiteBoxRig))
                nextMode = (int)ToneMode.FullRig;
            if (prof is null && (nextMode == (int)ToneMode.FullRig || nextMode == (int)ToneMode.AmpHeadOnly))
                nextMode = (int)ToneMode.WhiteBoxRig;
        }

        _router.Tone.Mode = (ToneMode)nextMode;
        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        _settings.ToneMode = nextMode;
        _settings.SelectedToneId = prof?.Id;
        _settings.Save();
        return new { selectedToneId = prof?.Id ?? "", customCabIrName = _router.Tone.CustomCabIrName, toneMode = nextMode };
    }

    private object DeleteTone(string? id)
    {
        var prof = string.IsNullOrWhiteSpace(id) ? null : _models.FirstOrDefault(t => t.Id == id);
        if (prof is null) return new { deleted = false };

        ToneProfileStore.Delete(prof);
        _models = ToneProfileStore.LoadAll();
        _router.Tone.SetModelLibrary(_models);
        var next = _models.FirstOrDefault();
        _router.Tone.ActiveProfile = next;

        int nextMode = Math.Clamp(_settings.ToneMode, 0, 6);
        if (nextMode != (int)ToneMode.SignalChain && next is null && (nextMode == (int)ToneMode.FullRig || nextMode == (int)ToneMode.AmpHeadOnly))
            nextMode = (int)ToneMode.WhiteBoxRig;
        _router.Tone.Mode = (ToneMode)nextMode;

        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        _settings.ToneMode = nextMode;
        _settings.SelectedToneId = next?.Id;
        _settings.Save();

        var dtos = ToModelDtos(_models);
        EmitEvent("tonesChanged", new { tones = dtos, selectedToneId = next?.Id ?? "", toneMode = nextMode });
        EmitLog($"[모델] '{prof.Name}' 삭제됨");
        return new { deleted = true, tones = dtos, selectedToneId = next?.Id ?? "", toneMode = nextMode };
    }

    private object ImportNamFile(JsonElement payload)
    {
        string? namPath = GetStr(payload, "namPath");
        string? customName = GetStr(payload, "name");
        if (string.IsNullOrWhiteSpace(namPath))
        {
            using var dlg = new OpenFileDialog
            {
                Title = "표준 .nam (Neural Amp Modeler) 모델 불러오기",
                Filter = "Neural Amp Modeler (.nam)|*.nam;*.json|모든 파일|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(_owner) != DialogResult.OK)
                return new { cancelled = true };
            namPath = dlg.FileName;
        }

        var loaded = ToneProfileStore.ImportNamFile(namPath, customName);
        _models = ToneProfileStore.LoadAll();
        loaded = _models.FirstOrDefault(t => t.Id == loaded.Id) ?? loaded;

        _router.Tone.SetModelLibrary(_models);
        _router.Tone.ActiveProfile = loaded;

        int slotIndex = GetInt(payload, "slotIndex", -1);
        SignalChainConfigDto? updatedChain = null;
        if (slotIndex >= 0 && slotIndex < SignalChainConfigDto.SlotCount && _settings.SignalChain?.Slots != null && slotIndex < _settings.SignalChain.Slots.Count)
        {
            var slot = _settings.SignalChain.Slots[slotIndex];
            if (string.Equals(slot.ModelId, "nam_capture", StringComparison.OrdinalIgnoreCase))
            {
                slot.NamModelId = loaded.Id;
                _router.Tone.Chain.UpdateChain(_settings.SignalChain);
                updatedChain = _router.Tone.Chain.GetConfigSnapshot();
            }
        }

        int nextMode = _settings.ToneMode == (int)ToneMode.SignalChain
            ? (int)ToneMode.SignalChain
            : _settings.ToneMode == (int)ToneMode.HybridPreampNam
                ? (int)ToneMode.HybridPreampNam
                : (_router.Tone.HasCustomCabIr ? (int)ToneMode.AmpHeadOnly : (int)ToneMode.FullRig);
        _router.Tone.Mode = (ToneMode)nextMode;

        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        _settings.ToneMode = nextMode;
        _settings.SelectedToneId = loaded.Id;
        _settings.Save();

        var dtos = ToModelDtos(_models);
        EmitEvent("tonesChanged", new { tones = dtos, selectedToneId = loaded.Id, toneMode = nextMode });
        if (updatedChain != null)
        {
            EmitEvent("signalChainChanged", new
            {
                signalChain = updatedChain,
                savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
                summary = $"슬롯 #{slotIndex + 1} NAM 할당: {loaded.Name}",
            });
        }
        EmitLog($"[모델] .nam 로드 완료 ({loaded.Architecture}): {loaded.Name} ({loaded.SampleRate} Hz)");
        return new
        {
            cancelled = false,
            profile = ToModelDtos(new[] { loaded })[0],
            tones = dtos,
            selectedToneId = loaded.Id,
            toneMode = nextMode,
            signalChain = updatedChain,
        };
    }

    private object ExportNamFile(JsonElement payload)
    {
        string? id = GetStr(payload, "toneId") ?? _router.Tone.ActiveProfile?.Id;
        var prof = string.IsNullOrWhiteSpace(id) ? _router.Tone.ActiveProfile : _models.FirstOrDefault(t => t.Id == id);
        if (prof is null)
            throw new InvalidOperationException("내보낼 .nam 모델이 선택되지 않았습니다.");

        using var dlg = new SaveFileDialog
        {
            Title = "표준 .nam 파일 내보내기",
            Filter = "Neural Amp Modeler (.nam)|*.nam",
            FileName = $"{prof.Name}.nam",
            DefaultExt = "nam",
        };
        if (dlg.ShowDialog(_owner) != DialogResult.OK)
            return new { cancelled = true, path = "" };

        ToneProfileStore.ExportNamFile(prof, dlg.FileName);
        EmitLog($"[내보내기] 표준 .nam 저장됨: {dlg.FileName}");
        return new { cancelled = false, path = dlg.FileName };
    }

    private object LoadCustomCabIr(JsonElement payload)
    {
        string? path = GetStr(payload, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            using var dlg = new OpenFileDialog
            {
                Title = "외부 캐비넷 IR (.wav) 선택",
                Filter = "캐비넷 IR WAV|*.wav|모든 파일|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(_owner) != DialogResult.OK)
                return new { cancelled = true, loaded = _router.Tone.HasCustomCabIr, customCabIrName = _router.Tone.CustomCabIrName };
            path = dlg.FileName;
        }

        var (name, taps) = _router.Tone.LoadCustomCabIr(path);
        _settings.CustomCabIrPath = path;
        _settings.Save();
        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        EmitLog($"[IR] 외부 캐비넷 IR 로드: {name} ({taps} taps)");
        return new { cancelled = false, loaded = true, customCabIrName = name, taps };
    }

    private object ClearCustomCabIr()
    {
        _router.Tone.ClearCustomCabIr();
        _settings.CustomCabIrPath = null;
        _settings.Save();
        SyncVisualizerCabIr();
        _wasIdleSettled = false;
        EmitLog("[IR] 외부 캐비넷 IR 해제 — 내장 4x12 V30 IR 복귀");
        return new { loaded = false, customCabIrName = "" };
    }

    private object LoadBackingTrack(JsonElement payload)
    {
        string? path = GetStr(payload, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            using var dlg = new OpenFileDialog
            {
                Title = "백킹 트랙 (MR) 오디오 파일 선택",
                Filter = "백킹 트랙 오디오|*.wav;*.mp3;*.flac;*.ogg;*.m4a|모든 파일|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(_owner) != DialogResult.OK)
                return new { cancelled = true, backing = _router.GetBackingState() };
            path = dlg.FileName;
        }

        var st = _router.LoadBackingTrack(path);
        _wasIdleSettled = false;
        return new { cancelled = false, backing = st };
    }

    private object ControlBackingTrack(JsonElement payload)
    {
        bool? playing = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("playing", out var pl) && (pl.ValueKind == JsonValueKind.True || pl.ValueKind == JsonValueKind.False)
            ? pl.GetBoolean()
            : null;
        bool? loop = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("loop", out var lp) && (lp.ValueKind == JsonValueKind.True || lp.ValueKind == JsonValueKind.False)
            ? lp.GetBoolean()
            : null;
        float? volume = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("volume", out var vl) && vl.TryGetSingle(out float vf)
            ? vf
            : null;
        double? seekSec = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("seekSec", out var sk) && sk.TryGetDouble(out double sf)
            ? sf
            : null;

        var st = _router.SetBackingControl(playing, loop, volume, seekSec);
        _wasIdleSettled = false;
        return new { backing = st };
    }

    private object UnloadBackingTrack()
    {
        _router.UnloadBackingTrack();
        _wasIdleSettled = false;
        return new { backing = _router.GetBackingState() };
    }

    private object SetTunerConfig(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("mute", out var mEl) && (mEl.ValueKind == JsonValueKind.True || mEl.ValueKind == JsonValueKind.False))
                _router.TunerMute = mEl.GetBoolean();
            if (payload.TryGetProperty("refA4Hz", out var aEl) && aEl.TryGetSingle(out float a4))
                _visualizer.TunerRefA4Hz = Math.Clamp(a4, 430f, 450f);
        }
        return new
        {
            mute = _router.TunerMute,
            refA4Hz = _visualizer.TunerRefA4Hz,
        };
    }

    private object StartGuitarAnalysis(JsonElement payload)
    {
        string? name = GetStr(payload, "name");
        _guitarDryCursor = -1;
        _wasIdleSettled = false;
        var st = _guitarAnalyzer.Start(name);
        EmitLog($"[기타 분석] 가이드 분석 시작: {name ?? "내 기타"}");
        return st;
    }

    private object CancelGuitarAnalysis()
    {
        _guitarAnalyzer.Cancel();
        return _guitarAnalyzer.GetLiveState();
    }

    private object SkipGuitarAnalysisStep()
    {
        _wasIdleSettled = false;
        return _guitarAnalyzer.SkipOrForceStep();
    }

    private object SaveGuitarProfile(JsonElement payload)
    {
        GuitarProfileDto? prof = null;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("profile", out var pEl) && pEl.ValueKind == JsonValueKind.Object)
            prof = JsonSerializer.Deserialize<GuitarProfileDto>(pEl.GetRawText(), JsonOpts);
        prof ??= _guitarAnalyzer.CompletedProfile;
        if (prof is null)
            throw new InvalidOperationException("저장할 기타 분석 결과가 없습니다.");

        prof = prof.Clone();
        string? nameOverride = GetStr(payload, "name");
        if (!string.IsNullOrWhiteSpace(nameOverride))
            prof.Name = nameOverride.Trim();
        if (string.IsNullOrWhiteSpace(prof.Id))
            prof.Id = $"G{DateTime.Now:yyyyMMddHHmmss}";
        if (string.IsNullOrWhiteSpace(prof.CreatedAt))
            prof.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        int idx = _settings.GuitarProfiles.FindIndex(g => string.Equals(g.Id, prof.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _settings.GuitarProfiles[idx] = prof;
        else _settings.GuitarProfiles.Insert(0, prof);

        _settings.ActiveGuitarProfileId = prof.Id;
        _settings.Save();
        _guitarAnalyzer.Cancel();

        EmitEvent("guitarProfilesChanged", new
        {
            guitarProfiles = _settings.GuitarProfiles,
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        });
        EmitLog($"[기타 분석] '{prof.Name}' 저장됨 ({prof.EstimatedPickupType}, 밝기 {prof.BrightnessScore}/100, Trim {prof.SuggestedInputTrimDb:+0.0;-0.0;0.0}dB)");

        return new
        {
            profile = prof,
            guitarProfiles = _settings.GuitarProfiles,
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        };
    }

    private object SelectGuitarProfile(string? id)
    {
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        if (!string.IsNullOrWhiteSpace(id) && _settings.GuitarProfiles.Any(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.ActiveGuitarProfileId = id;
            _settings.Save();
        }
        else if (string.IsNullOrWhiteSpace(id))
        {
            _settings.ActiveGuitarProfileId = null;
            _settings.Save();
        }

        EmitEvent("guitarProfilesChanged", new
        {
            guitarProfiles = _settings.GuitarProfiles,
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        });
        return new
        {
            guitarProfiles = _settings.GuitarProfiles,
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        };
    }

    private object DeleteGuitarProfile(string? id)
    {
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        if (!string.IsNullOrWhiteSpace(id))
        {
            _settings.GuitarProfiles.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_settings.ActiveGuitarProfileId, id, StringComparison.OrdinalIgnoreCase))
                _settings.ActiveGuitarProfileId = _settings.GuitarProfiles.FirstOrDefault()?.Id;
            _settings.Save();
        }

        EmitEvent("guitarProfilesChanged", new
        {
            guitarProfiles = _settings.GuitarProfiles,
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        });
        return new
        {
            guitarProfiles = _settings.GuitarProfiles,
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
        };
    }

    private object ApplyGuitarCalibration(string? id)
    {
        _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
        string? targetId = string.IsNullOrWhiteSpace(id) ? _settings.ActiveGuitarProfileId : id;
        var prof = string.IsNullOrWhiteSpace(targetId)
            ? null
            : _settings.GuitarProfiles.FirstOrDefault(g => string.Equals(g.Id, targetId, StringComparison.OrdinalIgnoreCase));
        if (prof is null)
            throw new InvalidOperationException("보정을 적용할 기타 프로필을 찾을 수 없습니다.");

        var chain = _router.Tone.Chain.GetConfigSnapshot();
        chain.InputGainDb = MathF.Round(Math.Clamp(prof.SuggestedInputTrimDb, -12f, 12f), 1);

        float gateDb = MathF.Round(Math.Clamp(prof.SuggestedGateDb, -80f, -15f), 1);
        for (int i = 0; i < chain.Slots.Count; i++)
        {
            var slot = chain.Slots[i];
            if (string.Equals(slot.ModelId, "gate_adaptive", StringComparison.OrdinalIgnoreCase))
            {
                slot.Enabled = true;
                slot.Params[0] = gateDb;
                if (chain.Scenes != null)
                {
                    foreach (var scene in chain.Scenes)
                    {
                        if (scene?.Slots != null && i < scene.Slots.Count)
                        {
                            scene.Slots[i].Enabled = true;
                            scene.Slots[i].Params[0] = gateDb;
                        }
                    }
                }
            }
        }

        _settings.ToneGateDb = Math.Clamp(prof.SuggestedGateDb, -80f, -20f);
        _router.Tone.GateThresholdDb = _settings.ToneGateDb;

        ApplyAndBroadcastMcpChain(
            chain,
            autoSave: false,
            $"기타 보정 적용 ({prof.Name}): Input {chain.InputGainDb:+0.0;-0.0;0.0}dB, Gate {gateDb:F0}dB",
            logTag: "기타 보정");

        return new
        {
            ok = true,
            inputGainDb = chain.InputGainDb,
            gateDb,
            signalChain = chain,
        };
    }

    private static List<ModelProfileDto> ToModelDtos(IEnumerable<ToneProfile> profiles)
    {
        return profiles.Select(p => new ModelProfileDto(
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
            p.HasCabIr
        )).ToList();
    }

    private void EmitLog(string line)
    {
        string clean = LogSanitizer.RecordLog(line);
        EmitEvent("log", new { line = $"[{DateTime.Now:HH:mm:ss}] {clean}" });
    }

    private void EmitEvent(string type, object payload) => EmitRaw(new { type, data = payload });

    private void EmitRaw(object message)
    {
        string json = JsonSerializer.Serialize(message, JsonOpts);
        RunOnUi(() => _sendJson(json));
    }

    private static string? GetStr(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int GetInt(JsonElement el, string prop, int def)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.TryGetInt32(out int n)
            ? n
            : def;

    private static float GetFloat(JsonElement el, string prop, float def)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.TryGetSingle(out float n)
            ? n
            : def;

    private static bool GetBool(JsonElement el, string prop, bool def)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : def;

    private object SetWindowTheme(JsonElement payload)
    {
        string bgHex = GetStr(payload, "bgHex") ?? "#F3F1EC";
        string borderHex = GetStr(payload, "borderHex") ?? "#C5C0B6";
        string textHex = GetStr(payload, "textHex") ?? "#151514";
        bool isDark = GetBool(payload, "isDark", false);

        RunOnUi(() =>
        {
            if (_owner is MainForm mf)
                mf.ApplyWindowTheme(bgHex, borderHex, textHex, isDark);
        });
        return new { ok = true };
    }

    private object GenerateAiToneFromUi(JsonElement payload)
    {
        string prompt = GetStr(payload, "prompt") ?? "";
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("원하는 톤 설명을 입력하세요.");
        bool autoSave = GetBool(payload, "autoSave", false);

        var curChain = _router.Tone.Chain.GetConfigSnapshot();
        var activeGuitar = GetActiveGuitarProfile();
        var res = _toneArchitect.GenerateViaBuiltinArchitect(prompt, curChain, activeGuitar);
        ApplyAndBroadcastMcpChain(res.Chain, autoSave, $"{res.PresetName} — {res.Summary}", logTag: "AI 아키텍트");
        return new
        {
            ok = true,
            presetName = res.PresetName,
            summary = res.Summary,
            highlights = res.GearHighlights,
            signalChain = res.Chain,
            savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
        };
    }

    private object BuildMidiSnapshot()
    {
        var learn = _midi.CurrentLearnTarget;
        return new
        {
            devices = MidiRouter.GetDeviceNames(),
            activeDevice = _midi.ActiveDeviceName,
            connected = _midi.IsConnected,
            bindings = _settings.MidiBindings ?? new List<MidiBindingDto>(),
            learning = learn is null ? null : new
            {
                targetKind = learn.TargetKind,
                slotIndex = learn.SlotIndex,
                paramIndex = learn.ParamIndex,
                sceneIndex = learn.SceneIndex,
            },
        };
    }

    private object SelectMidiDevice(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName == "__none__")
        {
            _midi.Disconnect();
            _settings.MidiInputName = "";
            _settings.Save();
        }
        else
        {
            bool ok = _midi.Connect(deviceName);
            _settings.MidiInputName = ok ? _midi.ActiveDeviceName : "";
            _settings.Save();
        }
        var snap = BuildMidiSnapshot();
        EmitEvent("midiStateChanged", snap);
        return snap;
    }

    private object StartMidiLearn(JsonElement payload)
    {
        string kind = GetStr(payload, "targetKind") ?? "param";
        int slotIdx = Math.Clamp(GetInt(payload, "slotIndex", 0), 0, 15);
        int paramIdx = Math.Clamp(GetInt(payload, "paramIndex", 0), 0, 7);
        int sceneIdx = Math.Clamp(GetInt(payload, "sceneIndex", 0), 0, 7);

        _midi.StartLearn(new MidiLearnTarget(kind, slotIdx, paramIdx, sceneIdx));
        EmitLog($"[MIDI Learn] 외부 MIDI 컨트롤러의 노브/페달/스위치를 움직이세요 ({kind})…");
        var snap = BuildMidiSnapshot();
        EmitEvent("midiStateChanged", snap);
        return snap;
    }

    private object CancelMidiLearn()
    {
        _midi.CancelLearn();
        var snap = BuildMidiSnapshot();
        EmitEvent("midiStateChanged", snap);
        return snap;
    }

    private object ClearMidiBinding(JsonElement payload)
    {
        string kind = GetStr(payload, "targetKind") ?? "param";
        int slotIdx = GetInt(payload, "slotIndex", -1);
        int paramIdx = GetInt(payload, "paramIndex", -1);
        int sceneIdx = GetInt(payload, "sceneIndex", -1);

        _settings.MidiBindings ??= new List<MidiBindingDto>();
        _settings.MidiBindings.RemoveAll(b =>
            string.Equals(b.TargetKind, kind, StringComparison.OrdinalIgnoreCase) &&
            (kind != "param" || (b.SlotIndex == slotIdx && b.ParamIndex == paramIdx)) &&
            (kind != "bypass" || b.SlotIndex == slotIdx) &&
            (kind != "scene" || b.SceneIndex == sceneIdx));
        _settings.Save();

        var snap = BuildMidiSnapshot();
        EmitEvent("midiStateChanged", snap);
        return snap;
    }

    private void OnMidiBindingLearned(MidiBindingDto binding)
    {
        RunOnUi(() =>
        {
            _settings.MidiBindings ??= new List<MidiBindingDto>();
            _settings.MidiBindings.RemoveAll(b =>
                b.Cc == binding.Cc ||
                (string.Equals(b.TargetKind, binding.TargetKind, StringComparison.OrdinalIgnoreCase) &&
                 b.SlotIndex == binding.SlotIndex &&
                 b.ParamIndex == binding.ParamIndex &&
                 b.SceneIndex == binding.SceneIndex));
            _settings.MidiBindings.Add(binding.Clone());
            _settings.Save();

            EmitLog($"[MIDI Learn] CC #{binding.Cc} 바인딩 완료 ({binding.TargetKind})");
            EmitEvent("midiStateChanged", BuildMidiSnapshot());
        });
    }

    private void OnMidiEventReceived(MidiEventMessage ev)
    {
        if (ev.IsProgramChange)
        {
            RunOnUi(() =>
            {
                var saved = _settings.SavedPresets ?? new List<SignalChainConfigDto>();
                if (ev.Number >= 0 && ev.Number < saved.Count)
                {
                    var target = saved[ev.Number].Clone();
                    ApplyAndBroadcastMcpChain(target, autoSave: false, $"MIDI PC #{ev.Number} → {target.PresetName}", logTag: "MIDI");
                }
                else if (ev.Number >= 0 && ev.Number < 8)
                {
                    var cur = _router.Tone.Chain.GetConfigSnapshot();
                    var switched = AntigravityToneArchitect.SwitchScene(cur, ev.Number);
                    ApplyAndBroadcastMcpChain(switched, autoSave: false, $"MIDI PC #{ev.Number} → Scene {(char)('A' + ev.Number)}", logTag: "MIDI");
                }
            });
            return;
        }

        var bindings = _settings.MidiBindings;
        if (bindings is null || bindings.Count == 0) return;

        var matched = bindings.FirstOrDefault(b => b.Cc == ev.Number && (b.Channel == 0 || b.Channel == ev.Channel));
        if (matched is null) return;

        if (matched.TargetKind == "tunerMute")
        {
            if (_midi.ShouldFireSwitchEdge(ev.Number, ev.Value))
            {
                bool nextMute = !_router.TunerMute;
                _router.TunerMute = nextMute;
                RunOnUi(() => EmitEvent("tunerMuteChanged", new { mute = nextMute }));
            }
            return;
        }

        if (matched.TargetKind == "looperRecDub")
        {
            if (_midi.ShouldFireSwitchEdge(ev.Number, ev.Value))
            {
                _router.Tone.Looper.TriggerRecordOrOverdub();
                var snap = _router.Tone.Looper.GetSnapshot();
                RunOnUi(() => EmitEvent("looperStateChanged", snap));
            }
            return;
        }

        if (matched.TargetKind == "looperPlayStop")
        {
            if (_midi.ShouldFireSwitchEdge(ev.Number, ev.Value))
            {
                _router.Tone.Looper.TriggerPlayStop();
                var snap = _router.Tone.Looper.GetSnapshot();
                RunOnUi(() => EmitEvent("looperStateChanged", snap));
            }
            return;
        }

        if (matched.TargetKind == "looperUndo")
        {
            if (_midi.ShouldFireSwitchEdge(ev.Number, ev.Value))
            {
                var lp = _router.Tone.Looper;
                if (lp.CanUndo) lp.Undo();
                else lp.Redo();
                var snap = lp.GetSnapshot();
                RunOnUi(() => EmitEvent("looperStateChanged", snap));
            }
            return;
        }

        if (matched.TargetKind == "tapTempo")
        {
            if (_midi.ShouldFireSwitchEdge(ev.Number, ev.Value))
            {
                var bpm = _midi.RegisterTapAndComputeBpm();
                if (bpm.HasValue)
                {
                    RunOnUi(() =>
                    {
                        var cur = _router.Tone.Chain.GetConfigSnapshot();
                        cur.TempoBpm = bpm.Value;
                        _settings.SignalChain = cur;
                        _router.Tone.Chain.UpdateChain(cur);
                        EmitEvent("signalChainChanged", new { signalChain = cur });
                    });
                }
            }
            return;
        }

        if (matched.TargetKind == "scene")
        {
            if (_midi.ShouldFireSwitchEdge(ev.Number, ev.Value))
            {
                RunOnUi(() =>
                {
                    var cur = _router.Tone.Chain.GetConfigSnapshot();
                    var next = AntigravityToneArchitect.SwitchScene(cur, matched.SceneIndex);
                    _settings.SignalChain = next;
                    _router.Tone.Chain.UpdateChain(next);
                    EmitEvent("signalChainChanged", new { signalChain = next, summary = $"MIDI CC #{ev.Number} → Scene {(char)('A' + matched.SceneIndex)}" });
                });
            }
            return;
        }

        if (matched.TargetKind == "bypass")
        {
            if (_midi.ShouldFireSwitchEdge(ev.Number, ev.Value))
            {
                RunOnUi(() =>
                {
                    var cur = AntigravityToneArchitect.EnsureScenes(_router.Tone.Chain.GetConfigSnapshot());
                    int sIdx = Math.Clamp(matched.SlotIndex, 0, 15);
                    if (!string.IsNullOrEmpty(cur.Slots[sIdx].ModelId))
                    {
                        bool nextEn = !cur.Slots[sIdx].Enabled;
                        cur.Slots[sIdx].Enabled = nextEn;
                        if (cur.Scenes is { Count: 8 })
                            cur.Scenes[Math.Clamp(cur.ActiveScene, 0, 7)].Slots[sIdx].Enabled = nextEn;
                        _settings.SignalChain = cur;
                        _router.Tone.Chain.UpdateChain(cur);
                        EmitEvent("signalChainChanged", new { signalChain = cur });
                    }
                });
            }
            return;
        }

        if (matched.TargetKind == "param")
        {
            // 연속 익스프레션 페달/노브 제어: 오디오 엔진에는 즉시 원자 스냅샷 반영, WebView2 UI 브로드캐스트는 30fps 쓰로틀링
            var cur = AntigravityToneArchitect.EnsureScenes(_router.Tone.Chain.GetConfigSnapshot());
            int sIdx = Math.Clamp(matched.SlotIndex, 0, 15);
            int pIdx = Math.Clamp(matched.ParamIndex, 0, 7);
            var slot = cur.Slots[sIdx];
            if (string.IsNullOrEmpty(slot.ModelId) || !AntigravityToneArchitect.Catalog.TryGetValue(slot.ModelId, out var meta))
                return;
            if (pIdx >= meta.Ranges.Length) return;

            var (mn, mx) = meta.Ranges[pIdx];
            float norm = Math.Clamp(ev.Value / 127f, 0f, 1f);
            float val = MathF.Round(mn + (mx - mn) * norm, 2);
            slot.Params[pIdx] = val;
            if (cur.Scenes is { Count: 8 })
                cur.Scenes[Math.Clamp(cur.ActiveScene, 0, 7)].Slots[sIdx].Params[pIdx] = val;

            _router.Tone.Chain.UpdateChain(cur);

            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastMidiUiBroadcastTick) >= 33)
            {
                Interlocked.Exchange(ref _lastMidiUiBroadcastTick, now);
                RunOnUi(() =>
                {
                    _settings.SignalChain = cur;
                    EmitEvent("signalChainChanged", new { signalChain = cur });
                });
            }
        }
    }

    private object HandleLooperCommand(JsonElement payload)
    {
        var looper = _router.Tone.Looper;
        if (looper.LoopLengthFrames == 0 && looper.State == LooperState.Empty && _router.EffectiveSampleRate <= 0)
            looper.Prepare(48000, maxSeconds: 60);

        string cmd = (GetStr(payload, "cmd") ?? "").Trim();
        switch (cmd)
        {
            case "recDub":
                if (!_router.IsRunning && looper.State == LooperState.Empty)
                    throw new InvalidOperationException("루퍼 녹음을 시작하려면 먼저 상단 [오디오 시작]을 켜세요.");
                looper.TriggerRecordOrOverdub();
                break;
            case "playStop":
                looper.TriggerPlayStop();
                break;
            case "clear":
                if (_router.IsRunning) looper.Clear();
                else looper.ResetAllImmediate();
                break;
            case "undo":
                looper.Undo();
                break;
            case "redo":
                looper.Redo();
                break;
            case "halfSpeed":
                looper.HalfSpeed = GetBool(payload, "value", !looper.HalfSpeed);
                break;
            case "reverse":
                looper.Reverse = GetBool(payload, "value", !looper.Reverse);
                break;
            case "quantize":
                looper.QuantizeSync = GetBool(payload, "value", !looper.QuantizeSync);
                break;
            case "level":
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out var lvEl) && lvEl.TryGetSingle(out float lv))
                    looper.Level = lv;
                break;
            default:
                throw new InvalidOperationException($"알 수 없는 루퍼 명령: {cmd}");
        }

        _wasIdleSettled = false;
        var snap = looper.GetSnapshot();
        EmitEvent("looperStateChanged", snap);
        return snap;
    }

    private object HandleRecorderCommand(JsonElement payload)
    {
        var rec = _router.Recorder;
        string cmd = (GetStr(payload, "cmd") ?? "").Trim();
        RecorderReampSnapshotDto snap;

        switch (cmd)
        {
            case "startRec":
                if (!_router.IsRunning)
                    throw new InvalidOperationException("Dual-Stream 녹음을 시작하려면 먼저 상단 [오디오 시작]을 켜세요.");
                snap = rec.StartRecording(GetStr(payload, "dir"), GetStr(payload, "name"));
                EmitLog($"[레코더] Dual-Stream 녹음 시작 (Dry DI + Stereo Wet → {snap.RecordingsDir})");
                break;

            case "stopRec":
                snap = rec.StopRecording();
                if (!string.IsNullOrEmpty(snap.LastDryName))
                    EmitLog($"[레코더] 저장 완료: {snap.LastDryName} + WetStereo.wav — 원클릭 리앰핑 준비됨!");
                break;

            case "toggleReamp":
            {
                bool nextEn = !rec.IsReampEnabled;
                snap = rec.SetReampState(enabled: nextEn, playing: nextEn);
                EmitLog(nextEn
                    ? $"[리앰핑] 원클릭 실시간 DI 리앰핑 활성화 ({snap.ReampName}) — 앰프·캐비넷·씬을 실시간으로 바꿔보세요."
                    : "[리앰핑] 실시간 DI 리앰핑 중지 (라이브 기타 입력 복귀)");
                break;
            }

            case "playPauseReamp":
            {
                bool nextPlay = !rec.IsReampPlaying;
                snap = rec.SetReampState(enabled: true, playing: nextPlay);
                break;
            }

            case "loopReamp":
                snap = rec.SetReampState(loop: GetBool(payload, "value", !rec.GetSnapshot().ReampLoop));
                break;

            case "seekReamp":
            {
                double seekSec = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out var sk) && sk.TryGetDouble(out double sv) ? sv : 0.0;
                snap = rec.SetReampState(seekSec: seekSec);
                break;
            }

            case "loadReampFile":
            {
                string? path = GetStr(payload, "path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    using var dlg = new OpenFileDialog
                    {
                        Title = "리앰핑할 Dry DI WAV 파일 선택",
                        Filter = "Dry DI 오디오|*.wav;*.flac;*.aif;*.aiff|모든 파일|*.*",
                        InitialDirectory = Directory.Exists(rec.RecordingsDirectory) ? rec.RecordingsDirectory : AppContext.BaseDirectory,
                        CheckFileExists = true,
                    };
                    if (dlg.ShowDialog(_owner) != DialogResult.OK)
                        return rec.GetSnapshot();
                    path = dlg.FileName;
                }
                snap = rec.LoadReampDryFile(path);
                EmitLog($"[리앰핑] Dry DI 로드 완료: {snap.ReampName} ({snap.ReampDurationSec:F1}s)");
                break;
            }

            case "openRecordingsFolder":
            {
                string dir = rec.RecordingsDirectory;
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
                snap = rec.GetSnapshot();
                break;
            }

            default:
                throw new InvalidOperationException($"알 수 없는 레코더 명령: {cmd}");
        }

        _wasIdleSettled = false;
        EmitEvent("recorderStateChanged", snap);
        return snap;
    }

    private object ExportNrigBundle(JsonElement payload)
    {
        var chain = _router.Tone.Chain.GetConfigSnapshot();
        string safeTitle = string.Concat(chain.PresetName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "NeuralRig_Bundle";

        string? destPath = GetStr(payload, "path");
        if (string.IsNullOrWhiteSpace(destPath))
        {
            using var dlg = new SaveFileDialog
            {
                Title = ".nrig 통합 리그 번들 내보내기",
                Filter = "NeuralRig 통합 번들 (.nrig)|*.nrig",
                FileName = $"{chain.PresetId}_{safeTitle}.nrig",
                DefaultExt = "nrig",
            };
            if (dlg.ShowDialog(_owner) != DialogResult.OK)
                return new { cancelled = true, path = "" };
            destPath = dlg.FileName;
        }

        GuitarProfileDto? activeGuitar = null;
        if (!string.IsNullOrWhiteSpace(_settings.ActiveGuitarProfileId) && _settings.GuitarProfiles != null)
            activeGuitar = _settings.GuitarProfiles.FirstOrDefault(g => string.Equals(g.Id, _settings.ActiveGuitarProfileId, StringComparison.OrdinalIgnoreCase));

        int sr = _router.EffectiveSampleRate > 0 ? _router.EffectiveSampleRate : 48000;
        var manifest = NrigBundleService.ExportBundle(
            destPath,
            chain,
            _router.Tone,
            _models,
            activeGuitar,
            author: GetStr(payload, "author"),
            description: GetStr(payload, "description"),
            sampleRate: sr);

        EmitLog($"[.nrig 내보내기] '{manifest.Name}' 번들 저장 완료 (NAM {manifest.NamModels.Count}종{(manifest.CustomCabIr != null ? " + 커스텀 IR" : "")}, SHA-256 검증 포함)");
        return new
        {
            cancelled = false,
            path = destPath,
            name = manifest.Name,
            namCount = manifest.NamModels.Count,
            hasCabIr = manifest.CustomCabIr != null,
        };
    }

    private object ImportNrigBundle(JsonElement payload)
    {
        string? nrigPath = GetStr(payload, "path");
        if (string.IsNullOrWhiteSpace(nrigPath))
        {
            using var dlg = new OpenFileDialog
            {
                Title = ".nrig 통합 리그 번들 불러오기",
                Filter = "NeuralRig 통합 번들 (.nrig)|*.nrig|모든 파일|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(_owner) != DialogResult.OK)
                return new { cancelled = true };
            nrigPath = dlg.FileName;
        }

        var res = NrigBundleService.ImportBundle(nrigPath, _router.Tone);
        return ApplyImportedRigAndNotify(res, $".nrig 번들 임포트: {res.SignalChain.PresetName}", saveAsUserPreset: true);
    }

    private object ImportDroppedFile(JsonElement payload)
    {
        string fileName = GetStr(payload, "fileName") ?? "dropped_file.nam";
        string b64 = GetStr(payload, "base64Data") ?? "";
        if (string.IsNullOrWhiteSpace(b64))
            throw new InvalidOperationException("드롭된 파일 데이터가 비어 있습니다.");

        int comma = b64.IndexOf(',');
        if (comma >= 0) b64 = b64[(comma + 1)..];
        byte[] bytes = Convert.FromBase64String(b64);

        var res = ToneHubService.ImportRawFileBytes(fileName, bytes, _router.Tone);
        return ApplyImportedRigAndNotify(res, $"드래그앤드롭 로드: {fileName}", saveAsUserPreset: Path.GetExtension(fileName).Equals(".nrig", StringComparison.OrdinalIgnoreCase));
    }

    private object GetToneHubCatalog()
    {
        var items = ToneHubService.GetCatalog(_models, _settings.SavedPresets, _router.Tone.CustomCabIrName);
        var dawPlugin = DawPluginExporter.GetPluginInspectorInfo();
        return new
        {
            items,
            dawPlugin,
        };
    }

    private object InstallToneHubItem(JsonElement payload)
    {
        string id = GetStr(payload, "id") ?? throw new InvalidOperationException("Tone Hub 항목 ID가 필요합니다.");
        int slotIdx = GetInt(payload, "targetSlotIndex", -1);
        var res = ToneHubService.InstallHubItem(id, _router.Tone, slotIdx);

        var fakeImport = new NrigImportResultDto(
            new NrigManifestDto { Name = res.Item.Title, SignalChain = res.AppliedChain ?? _router.Tone.Chain.GetConfigSnapshot() },
            res.AppliedChain ?? _router.Tone.Chain.GetConfigSnapshot(),
            res.InstalledModel != null ? new List<ToneProfile> { res.InstalledModel } : new List<ToneProfile>(),
            res.CustomCabIrPath,
            res.CustomCabIrName,
            null);

        ApplyImportedRigAndNotify(
            fakeImport,
            res.Summary,
            saveAsUserPreset: string.Equals(res.Item.Category, "nrig", StringComparison.OrdinalIgnoreCase));

        return new
        {
            summary = res.Summary,
            catalog = ToneHubService.GetCatalog(_models, _settings.SavedPresets, _router.Tone.CustomCabIrName),
        };
    }

    private async Task<object> InstallToneHubUrlAsync(JsonElement payload)
    {
        string url = GetStr(payload, "url") ?? throw new InvalidOperationException("다운로드할 URL을 입력하세요.");
        var res = await ToneHubService.InstallFromUrlAsync(url, _router.Tone, ct: _mcpCts.Token).ConfigureAwait(false);

        return await RunOnUiAsync(() =>
        {
            var data = ApplyImportedRigAndNotify(res, $"원격 URL 설치 완료: {res.Manifest.Name}", saveAsUserPreset: url.EndsWith(".nrig", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(data);
        }, _mcpCts.Token).ConfigureAwait(false);
    }

    private object ExportDawPlugin(JsonElement payload)
    {
        string? outDir = GetStr(payload, "outputDir");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "NeuralRig Studio VST3 (.vst3) 및 CLAP (.clap) DAW 플러그인 패키지를 내보낼 폴더를 선택하세요.",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppContext.BaseDirectory, "DawPlugins"),
            };
            if (dlg.ShowDialog(_owner) != DialogResult.OK)
                return new { cancelled = true };
            outDir = dlg.SelectedPath;
        }

        var res = DawPluginExporter.ExportPluginPackage(
            outDir,
            _router.Tone.Chain.GetConfigSnapshot(),
            _router.Tone,
            _models);

        EmitLog($"[DAW 플러그인] VST3 & CLAP 패키지 생성 완료 → {res.OutputDirectory}");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = res.OutputDirectory,
                UseShellExecute = true,
            });
        }
        catch { }

        return new
        {
            cancelled = false,
            outputDirectory = res.OutputDirectory,
            vst3BundlePath = res.Vst3BundlePath,
            clapPluginPath = res.ClapPluginPath,
            defaultRigPath = res.DefaultRigPath,
            parameterCount = res.ParameterCount,
        };
    }

    private object ApplyImportedRigAndNotify(NrigImportResultDto res, string summary, bool saveAsUserPreset)
    {
        _models = ToneProfileStore.LoadAll();
        _router.Tone.SetModelLibrary(_models);

        var chain = res.SignalChain.Clone();
        _settings.ToneMode = (int)ToneMode.SignalChain;
        _settings.SignalChain = chain;

        if (!string.IsNullOrWhiteSpace(res.CustomCabIrPath))
            _settings.CustomCabIrPath = res.CustomCabIrPath;

        if (res.ImportedModels.Count > 0)
        {
            _router.Tone.ActiveProfile = res.ImportedModels[0];
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
        _router.Tone.Chain.UpdateChain(chain);
        _router.Tone.Mode = ToneMode.SignalChain;
        SyncVisualizerCabIr();
        _wasIdleSettled = false;

        var toneDtos = ToModelDtos(_models);
        string selTone = _router.Tone.ActiveProfile?.Id ?? "";
        EmitEvent("tonesChanged", new { tones = toneDtos, selectedToneId = selTone, toneMode = (int)ToneMode.SignalChain });
        if (!string.IsNullOrWhiteSpace(res.CustomCabIrName))
            EmitEvent("cabIrChanged", new { customCabIrName = res.CustomCabIrName });
        if (res.GuitarProfile != null)
            EmitEvent("guitarProfilesChanged", new { guitarProfiles = _settings.GuitarProfiles, activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "" });
        EmitEvent("signalChainChanged", new
        {
            signalChain = chain,
            savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            summary,
        });
        EmitLog($"[통합 허브] {summary}");

        return new
        {
            cancelled = false,
            signalChain = chain,
            savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            tones = toneDtos,
            selectedToneId = selTone,
            customCabIrName = res.CustomCabIrName,
            summary,
        };
    }

    private RigBenchmarkReportDto RunBenchmarkSuite(JsonElement payload)
    {
        int sr = _router.EffectiveSampleRate > 0 ? _router.EffectiveSampleRate : 48000;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("sampleRate", out var srEl) &&
            srEl.TryGetInt32(out int reqSr) &&
            reqSr is >= 44100 and <= 192000)
        {
            sr = reqSr;
        }

        double? liveRtMs = _router.IsRunning ? _router.GetLatency()?.TotalMs : null;
        var report = RigBenchmarkSuite.Run(sr, liveRtMs);

        string liveTag = report.LiveAudioRoundTripMs.HasValue
            ? $" · LiveRT={report.LiveAudioRoundTripMs.Value:F1}ms"
            : "";
        EmitLog(
            $"[Benchmark] {(report.OverallPassed ? "PASS" : "FAIL")} · Hardware={report.OverallSimilarityPct:F1}% · " +
            $"64fr={(report.MinBuffer64Pass ? "OK" : "OVER")} · RTAlloc={(report.ZeroRtAllocAll ? "0B" : "NON-ZERO")}{liveTag} · {report.TotalDurationMs:F0}ms");
        return report;
    }

    private object UpdateOnboardingState(JsonElement payload)
    {
        bool cloudPromptChanged = false;
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
            if (payload.TryGetProperty("lastGuestLoginPromptDate", out var guestDateEl) &&
                guestDateEl.ValueKind == JsonValueKind.String)
            {
                _settings.LastGuestLoginPromptDate = guestDateEl.GetString()?.Trim();
                cloudPromptChanged = true;
            }
            if (payload.TryGetProperty("hasShownSponsorPrompt", out var sponsorEl))
            {
                if (sponsorEl.ValueKind == JsonValueKind.True)
                {
                    _settings.HasShownSponsorPrompt = true;
                    cloudPromptChanged = true;
                }
                else if (sponsorEl.ValueKind == JsonValueKind.False)
                {
                    _settings.HasShownSponsorPrompt = false;
                    cloudPromptChanged = true;
                }
            }
            _settings.Save();
            if (cloudPromptChanged)
                EmitEvent("cloudStateChanged", _cloud.GetState(_settings));
        }
        return new
        {
            hasChosenAuthMode = _settings.HasChosenAuthMode,
            hasCompletedTutorial = _settings.HasCompletedTutorial,
            lastGuestLoginPromptDate = _settings.LastGuestLoginPromptDate,
            hasShownSponsorPrompt = _settings.HasShownSponsorPrompt,
        };
    }

    private static object OpenExternalUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("유효한 HTTPS 외부 URL만 열 수 있습니다.");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });
        return new { ok = true, url = uri.AbsoluteUri };
    }

    private async Task<CloudStateDto> SignInGoogleAndNotifyAsync()
    {
        var state = await _cloud.StartGoogleSignInAsync(_settings);
        EmitEvent("cloudStateChanged", state);
        EmitLog($"[Cloud] Google 계정 로그인 완료: {state.User?.DisplayName ?? "Player"}");
        _ = Task.Run(() => _cloud.FlushPendingBugReportsAsync(_settings));
        return state;
    }

    private CloudStateDto ContinueGuestAndNotify()
    {
        var state = _cloud.ContinueAsGuest(_settings);
        EmitEvent("cloudStateChanged", state);
        return state;
    }

    private CloudStateDto SignOutAndNotify()
    {
        var state = _cloud.SignOut(_settings);
        EmitEvent("cloudStateChanged", state);
        EmitLog("[Cloud] 로그아웃되어 게스트 모드로 전환되었습니다.");
        return state;
    }

    private async Task<CloudStateDto> CloudSyncNowAsync()
    {
        var state = await _cloud.SyncSettingsAsync(
            _settings,
            CustomDeviceRegistry.GetAll(),
            (remotePresets, remoteGuitars, remoteDevices) =>
            {
                if (remotePresets is { Count: > 0 })
                {
                    _settings.SavedPresets ??= new List<SignalChainConfigDto>();
                    foreach (var p in remotePresets)
                    {
                        if (!string.IsNullOrWhiteSpace(p.PresetId) &&
                            !_settings.SavedPresets.Any(x => string.Equals(x.PresetId, p.PresetId, StringComparison.OrdinalIgnoreCase)))
                        {
                            _settings.SavedPresets.Add(p);
                        }
                    }
                }
                if (remoteGuitars is { Count: > 0 })
                {
                    _settings.GuitarProfiles ??= new List<GuitarProfileDto>();
                    foreach (var g in remoteGuitars)
                    {
                        if (!string.IsNullOrWhiteSpace(g.Id) &&
                            !_settings.GuitarProfiles.Any(x => string.Equals(x.Id, g.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            _settings.GuitarProfiles.Add(g);
                        }
                    }
                }
                if (remoteDevices is { Count: > 0 })
                {
                    foreach (var d in remoteDevices)
                    {
                        if (!string.IsNullOrWhiteSpace(d.Id) && !CustomDeviceRegistry.TryGet(d.Id, out _))
                        {
                            try
                            {
                                CloudPlatformService.ValidateCustomDeviceSpecSafety(d);
                                CustomDeviceRegistry.RegisterOrUpdate(d);
                            }
                            catch { }
                        }
                    }
                }
                _settings.Save();
                EmitEvent("customDevicesChanged", new { customDevices = CustomDeviceRegistry.GetAll() });
                EmitEvent("guitarProfilesChanged", new
                {
                    guitarProfiles = _settings.GuitarProfiles ?? new List<GuitarProfileDto>(),
                    activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
                });
            });
        EmitEvent("cloudStateChanged", state);
        EmitLog("[Cloud] 설정 · 프리셋 · 커스텀 기어 클라우드 동기화 완료");
        return state;
    }

    private async Task<object> GetCommunityHubAsync(JsonElement payload)
    {
        string cat = GetStr(payload, "category") ?? "all";
        string q = GetStr(payload, "query") ?? "";
        string sort = GetStr(payload, "sort") ?? "downloads";
        var (items, source) = await _cloud.GetCommunityHubAsync(_settings, cat, q, sort);
        return new { items, source };
    }

    private async Task<object> UploadCommunityGearAsync(JsonElement payload)
    {
        string deviceId = GetStr(payload, "deviceId") ?? "";
        string summary = GetStr(payload, "summary") ?? "";
        var tags = new List<string>();
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("tags", out var tagsEl) &&
            tagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tagsEl.EnumerateArray())
                if (t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                    tags.Add(t.GetString()!);
        }
        if (!CustomDeviceRegistry.TryGet(deviceId, out var spec) || spec == null)
            throw new InvalidOperationException($"커스텀 기어 '{deviceId}'를 찾을 수 없습니다.");

        return await _cloud.UploadCommunityGearAsync(_settings, spec, summary, tags.ToArray());
    }

    private async Task<object> InstallCommunityGearAsync(JsonElement payload)
    {
        var item = JsonSerializer.Deserialize<CommunityGearItemDto>(payload.GetRawText(), JsonOpts)
            ?? throw new InvalidOperationException("Invalid community gear payload.");

        var installed = await _cloud.InstallCommunityGearAsync(_settings, item);
        EmitEvent("customDevicesChanged", new
        {
            customDevices = CustomDeviceRegistry.GetAll(),
            addedDevice = installed,
        });
        EmitLog($"[Community Hub] '{installed.OriginalName}' 설치 및 SPICE 검증 완료");
        return new { ok = true, addedDevice = installed };
    }

    private async Task<object> SubmitBugReportAsync(JsonElement payload)
    {
        string title = GetStr(payload, "title") ?? "";
        string category = GetStr(payload, "category") ?? "general";
        string severity = GetStr(payload, "severity") ?? "normal";
        string description = GetStr(payload, "description") ?? "";

        var diag = new Dictionary<string, string>
        {
            ["appVersion"] = OtaUpdateService.CurrentVersion,
            ["osVersion"] = Environment.OSVersion.VersionString,
            ["cpuCount"] = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["avx2Supported"] = System.Runtime.Intrinsics.X86.Avx2.IsSupported ? "true" : "false",
            ["fmaSupported"] = System.Runtime.Intrinsics.X86.Fma.IsSupported ? "true" : "false",
            ["isAudioRunning"] = _router.IsRunning ? "true" : "false",
            ["inputKind"] = _settings.InputKind ?? "",
            ["outputKind"] = _settings.OutputKind ?? "",
            ["sampleRate"] = _router.EffectiveSampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["customDeviceCount"] = CustomDeviceRegistry.GetAll().Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var result = await _cloud.SubmitBugReportAsync(_settings, title, category, severity, description, diag);
        EmitEvent("cloudStateChanged", _cloud.GetState(_settings));
        return result;
    }

    private async Task<object> ApplyOtaUpdateAsync(JsonElement payload)
    {
        var info = JsonSerializer.Deserialize<OtaCheckResultDto>(payload.GetRawText(), JsonOpts)
            ?? await OtaUpdateService.CheckForUpdateAsync(CloudPlatformService.ResolveCloudBaseUrl(_settings));
        var (ok, message, launchedPath) = await OtaUpdateService.DownloadAndStageUpdateAsync(info);
        EmitLog($"[OTA] {message}");
        return new { ok, message, launchedPath };
    }

    public void Dispose()
    {
        try { _mcpCts.Cancel(); } catch { }
        try { _mcpCts.Dispose(); } catch { }
        try { _midi.Dispose(); } catch { }
        try { _router.Recorder.Dispose(); } catch { }
        try { _panelDev?.Dispose(); } catch { }
        _panelDev = null;
    }
}
