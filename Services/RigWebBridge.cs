using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NeuralRigStudio.Audio;

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
    private readonly float[] _guitarChunk = new float[4096];
    private long _guitarDryCursor = -1;
    private readonly CancellationTokenSource _mcpCts = new();

    private AppSettings _settings;
    private List<ToneProfile> _models = new();
    private AsioDevice? _panelDev;
    private bool _audioBusy;
    private float _meterL;
    private float _meterR;
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
        _router.StateChanged += s => EmitEvent("audioState", new { isRunning = _router.IsRunning, busy = _audioBusy, state = s });
        _router.StoppedUnexpectedly += reason =>
        {
            RunOnUi(() =>
            {
                EmitEvent("audioState", new { isRunning = false, busy = false, state = $"예기치 않게 중지됨: {reason}", error = reason });
            });
        };

        LoadInitialState();
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
        _router.Tone.Chain.UpdateChain(_settings.SignalChain);

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
                var gp100 = AudioRouter.GetDriverNames().FirstOrDefault(IsValetonGp100Driver);
                if (gp100 != null)
                {
                    _settings.InputDriver = gp100;
                    if (_settings.ChannelMode == 0) _settings.ChannelMode = 1;
                    _settings.Save();
                }
            }
            catch { }
        }
    }

    private static bool IsValetonGp100Driver(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Contains("Valeton", StringComparison.OrdinalIgnoreCase)
         || name.Contains("GP-100", StringComparison.OrdinalIgnoreCase)
         || name.Contains("GP100", StringComparison.OrdinalIgnoreCase));

    private static bool IsGuitarInterfaceDriver(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (IsValetonGp100Driver(name)) return true;
        return name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Steinberg", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Universal Audio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Audient", StringComparison.OrdinalIgnoreCase)
            || name.Contains("MOTU", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Roland", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BOSS", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Line 6", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Neural", StringComparison.OrdinalIgnoreCase);
    }

    public void EmitTelemetryTick()
    {
        var (l, r, clip) = _router.TakePeaks();
        _meterL = Math.Max(l, _meterL * 0.85f);
        _meterR = Math.Max(r, _meterR * 0.85f);
        if (_meterL < 0.0005f) _meterL = 0f;
        if (_meterR < 0.0005f) _meterR = 0f;

        var backing = _router.GetBackingState();
        bool running = _router.IsRunning;
        bool settled = !running && !backing.Playing && !_guitarAnalyzer.IsActive && _meterL == 0f && _meterR == 0f && !clip;
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
            savedPresets = _settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            guitarProfiles = _settings.GuitarProfiles ?? new List<GuitarProfileDto>(),
            activeGuitarProfileId = _settings.ActiveGuitarProfileId ?? "",
            tunerMute = _router.TunerMute,
            tunerRefA4Hz = _visualizer.TunerRefA4Hz,
            backing = _router.GetBackingState(),
            isAudioRunning = _router.IsRunning,
        };
    }

    private object BuildAudioDevicesSnapshot()
    {
        var sortedDrivers = AudioRouter.GetDriverNames()
            .OrderByDescending(IsValetonGp100Driver)
            .ThenByDescending(IsGuitarInterfaceDriver)
            .ThenByDescending(d => !d.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var wasapi = new List<object> { new { id = "", label = "시스템 기본 출력 장치" } };
        try
        {
            var en = new MMDeviceEnumerator();
            string defaultId = "";
            try { defaultId = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console).ID; } catch { }
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                string label = d.FriendlyName + (d.ID == defaultId ? " (기본)" : "");
                wasapi.Add(new { id = d.ID, label });
            }
        }
        catch (Exception ex)
        {
            EmitLog($"WASAPI 목록 조회 실패: {ex.Message}");
        }

        string? initialDriver = !string.IsNullOrEmpty(_settings.InputDriver) && sortedDrivers.Contains(_settings.InputDriver)
            ? (_settings.InputDriver.Contains("Realtek", StringComparison.OrdinalIgnoreCase) && sortedDrivers.Any(IsValetonGp100Driver)
                ? sortedDrivers.First(IsValetonGp100Driver)
                : _settings.InputDriver)
            : sortedDrivers.FirstOrDefault();

        object? initialCaps = !string.IsNullOrEmpty(initialDriver) && !_router.IsRunning
            ? GetAsioCapabilities(initialDriver)
            : null;

        return new
        {
            asioDrivers = sortedDrivers,
            wasapiDevices = wasapi,
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

        string? inputDriver = GetStr(payload, "inputDriver");
        if (string.IsNullOrWhiteSpace(inputDriver)
            || inputDriver.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
        {
            var gp100 = AudioRouter.GetDriverNames().FirstOrDefault(IsValetonGp100Driver);
            if (gp100 != null)
            {
                if (string.IsNullOrWhiteSpace(inputDriver))
                    EmitLog($"[GP-100] 자동 감지됨: '{gp100}' 우선 연결");
                else
                    EmitLog($"[GP-100] '{inputDriver}' 대신 연결된 '{gp100}' 우선 선택");
                inputDriver = gp100;
            }
        }
        if (string.IsNullOrWhiteSpace(inputDriver))
            throw new InvalidOperationException("ASIO 입력 드라이버를 선택하세요.");

        int channelMode = GetInt(payload, "channelMode", 1);
        int bufSize = GetInt(payload, "asioInBufferSize", 0);
        string outputKind = GetStr(payload, "outputKind") ?? "WASAPI";
        bool exclusive = GetBool(payload, "wasapiExclusive", false);
        string? wasapiId = GetStr(payload, "wasapiDeviceId");
        if (string.IsNullOrWhiteSpace(wasapiId)) wasapiId = null;
        string? asioOutDriver = GetStr(payload, "asioOutDriver");
        int presetIdx = Math.Clamp(GetInt(payload, "presetIndex", 1), 0, Presets.Length - 1);
        var preset = Presets[presetIdx];

        _settings.InputDriver = inputDriver;
        _settings.ChannelMode = channelMode;
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
        bool isAnyAsio = kind != OutputKind.Wasapi;

        var cfg = new RouterConfig
        {
            InputDriver = inputDriver,
            ChannelMode = channelMode,
            AsioInBufferSize = bufSize,
            Kind = kind,
            Exclusive = exclusive,
            WasapiDeviceId = wasapiId,
            AsioOutDriver = asioOutDriver,
            RingTargetMs = kind == OutputKind.AsioDirectDuplex ? 0 : preset.RingMs,
            WasapiLatencyMs = isAnyAsio ? 0 : (exclusive ? preset.ExclMs : preset.SharedMs),
        };

        _audioBusy = true;
        EmitEvent("audioState", new { isRunning = false, busy = true, state = "오디오 엔진 초기화 중…" });
        try
        {
            try
            {
                await _router.StartAsync(cfg);
            }
            catch (Exception ex) when (!IsValetonGp100Driver(inputDriver) && inputDriver.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            {
                var gp100 = AudioRouter.GetDriverNames().FirstOrDefault(IsValetonGp100Driver);
                if (gp100 is null) throw;
                EmitLog($"[GP-100] '{inputDriver}' 시작 실패({ex.Message}) → '{gp100}'로 자동 전환 시도");
                inputDriver = gp100;
                cfg.InputDriver = gp100;
                _settings.InputDriver = gp100;
                await _router.StartAsync(cfg);
            }

            _settings.Save();
            SyncVisualizerCabIr();
            string okText = kind == OutputKind.AsioDirectDuplex
                ? "ASIO 직결 풀듀플렉스 초저지연 모니터링 중"
                : "실시간 저지연 모니터링 중";
            EmitEvent("audioState", new { isRunning = true, busy = false, state = okText });
            return new { isRunning = true, inputDriver };
        }
        catch (Exception ex)
        {
            EmitEvent("audioState", new { isRunning = false, busy = false, state = "시작 실패" });
            if (IsValetonGp100Driver(inputDriver))
                throw new InvalidOperationException($"Valeton GP-100 ({inputDriver}) 연결 실패: GP-100 전원이 켜져 있고 USB 케이블이 PC에 연결되어 있는지 확인하세요. ({ex.Message})", ex);
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
        var res = _toneArchitect.GenerateViaBuiltinArchitect(prompt, curChain);
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
        JsonElement paramsEl = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("params", out var pEl) ? pEl : default;

        var cur = _router.Tone.Chain.GetConfigSnapshot();
        var updated = AntigravityToneArchitect.ApplySlotEdit(cur, slotIndex, modelId, enabled, paramsEl);
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

        var cur = _router.Tone.Chain.GetConfigSnapshot();
        var updated = AntigravityToneArchitect.SwitchScene(cur, sceneIdx, routingMode, parallelMix);
        ApplyAndBroadcastMcpChain(updated, autoSave: false, $"Scene {((char)('A' + updated.ActiveScene))} / {(updated.RoutingMode == 1 ? "Parallel" : "Serial")} 전환");
        return new { activeScene = updated.ActiveScene, routingMode = updated.RoutingMode, parallelMix = updated.ParallelMix, signalChain = updated };
    }

    private async Task<object> ExecuteMcpControlAudioAsync(JsonElement args)
    {
        string action = (GetStr(args, "action") ?? "status").Trim().ToLowerInvariant();
        if (action == "stop")
            return StopMonitoring();
        if (action is "start" or "start_gp100_wasapi" or "start_gp100_duplex")
        {
            if (_router.IsRunning)
                return new { isRunning = true, inputDriver = _settings.InputDriver ?? "" };

            var gpDriver = AudioRouter.GetDriverNames().FirstOrDefault(IsValetonGp100Driver);
            var payloadObj = new
            {
                inputDriver = gpDriver ?? _settings.InputDriver ?? "",
                channelMode = gpDriver != null || action.StartsWith("start_gp100") ? 1 : _settings.ChannelMode,
                asioInBufferSize = _settings.AsioInBufferSize,
                outputKind = action == "start_gp100_duplex" ? "ASIO_DUPLEX" : action == "start_gp100_wasapi" ? "WASAPI" : _settings.OutputKind,
                wasapiExclusive = _settings.WasapiExclusive,
                wasapiDeviceId = _settings.WasapiDeviceId ?? "",
                asioOutDriver = _settings.AsioOutDriver ?? "",
                presetIndex = gpDriver != null ? 1 : _settings.PresetIndex,
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

        _router.Tone.ActiveProfile = loaded;
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
        EmitLog($"[모델] .nam 로드 완료: {loaded.Name} ({loaded.SampleRate} Hz)");
        return new
        {
            cancelled = false,
            profile = ToModelDtos(new[] { loaded })[0],
            tones = dtos,
            selectedToneId = loaded.Id,
            toneMode = nextMode,
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

    private void EmitLog(string line) => EmitEvent("log", new { line = $"[{DateTime.Now:HH:mm:ss}] {line}" });

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

    public void Dispose()
    {
        try { _mcpCts.Cancel(); } catch { }
        try { _mcpCts.Dispose(); } catch { }
        try { _panelDev?.Dispose(); } catch { }
        _panelDev = null;
    }
}
