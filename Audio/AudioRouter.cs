using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace NeuralRigStudio.Audio;

public enum InputBackendKind
{
    Asio = 0,
    Wasapi = 1,
}

public enum OutputKind
{
    Wasapi,
    AsioOut,
    AsioDirectDuplex,
}

public sealed class RouterConfig
{
    public InputBackendKind InputKind = InputBackendKind.Asio;
    public string InputDriver = "";
    public string? WasapiInDeviceId;     // null/"" = 시스템 기본 녹음 장치
    public bool WasapiInExclusive;       // WASAPI 입력 단독 모드
    public bool WasapiInRaw = true;      // Windows 소음 억제/AGC APO 우회 (기타 서스테인 보호)
    public int ChannelMode = 1;          // 0: 스테레오(Offset, Offset+1), 1: 모노(Offset), 2: 모노 Ch2(하위호환)
    public int InputChannelOffset = 0;   // 다채널 인터페이스 0-based 시작 채널
    public int AsioInBufferSize;         // 0 = 드라이버 선호 크기
    public OutputKind Kind = OutputKind.Wasapi;
    public bool Exclusive;               // WASAPI 출력 단독 모드
    public string? WasapiDeviceId;       // null = 시스템 기본 출력 장치
    public string? AsioOutDriver;
    public int RingTargetMs = 16;        // 링 프리롤/목표 지연
    public int WasapiLatencyMs = 25;     // WASAPI 버퍼 힌트
}

public readonly record struct LatencyInfo(double InMs, double RingMs, double OutMs, double TotalMs);

public sealed record BackingTrackState(
    bool Loaded,
    bool Playing,
    bool Loop,
    float Volume,
    double PositionSec,
    double DurationSec,
    string Name,
    string Path);

internal sealed record BackingBuffer(float[] Left, float[] Right, int SampleRate, string Name, string Path)
{
    public int Length => Left.Length;
}

/// <summary>
/// 범용 오디오 입력(ASIO 다채널 오디오 인터페이스 / 멀티이펙터 USB ASIO / Windows WASAPI USB 케이블·라인인·마이크)
/// → (단일 ASIO 풀듀플렉스 직결 OR Lock-free 링 버퍼 → WASAPI / 제2 ASIO) 초저지연 오디오 라우터.
/// + Lock-free MR 백킹 트랙 믹서 + 50fps 비주얼라이저 / MPM 크로매틱 튜너 스냅샷 버퍼.
/// </summary>
public sealed class AudioRouter : IAudioTelemetrySource
{
    private AsioDevice? _in;
    private WasapiRecorder? _inWasapi;
    private AsioDevice? _outAsio;
    private WasapiPlayer? _wasapi;
    private RingBuffer? _ring;
    private RingSource? _source;
    private float[]? _scratchL;
    private float[]? _scratchR;
    private CancellationTokenSource? _stopCts;

    private int _rate;
    private int _inSelChannels;      // 1 or 2
    private int _inFrames;           // 입력 버퍼 프레임 추정치
    private double _usedInLatencyMs;
    private double _usedOutLatencyMs;
    private bool _directDuplex;
    private bool _usingWasapiInput;
    private int _runningFlag;
    private int _stopping;

    // WASAPI 입력 제로 할당 패킷 디코딩 상태 (시작 시 1회 캐싱)
    private int _wasapiInChannels = 2;
    private int _wasapiInBytesPerFrame = 8;
    private bool _wasapiInIsFloat = true;
    private int _wasapiInBits = 32;
    private int _wasapiCh0;
    private int _wasapiCh1;

    private long _pkL;               // double 비트 저장
    private long _pkR;
    private long _clipTick;
    private long _inClipTick;
    private bool _scratchWarned;

    // ── 실시간 AVX2 WaveNet / 4x WhiteBox 톤 프로세서 + Dual-Stream 레코더/리앰퍼 ──
    public ToneProcessor Tone { get; } = new();
    public DualStreamRecorder Recorder { get; } = new();

    // ── 백킹 트랙 (MR 합주) 락프리 믹서 ──
    private BackingBuffer? _backing;
    private int _backingPlaying;
    private int _backingLoop = 1;
    private float _backingVolume = 0.7f;
    private int _backingPos;
    private int _backingSeekReq = -1;

    // ── 비주얼라이저(포스트 톤) + 크로매틱 튜너 / 기타 분석(드라이 기타) 락프리 링 버퍼 ──
    private const int VisCap = 8192;
    private readonly float[] _visRing = new float[VisCap];
    private int _visPos;
    private readonly float[] _dryVisRing = new float[VisCap];
    private int _dryVisPos;
    private long _dryTotalWritten;
    private int _tunerMute;

    public bool TunerMute
    {
        get => Volatile.Read(ref _tunerMute) == 1;
        set => Volatile.Write(ref _tunerMute, value ? 1 : 0);
    }

    public event Action<string>? Log;
    public event Action<string>? StateChanged;
    public event Action<string>? StoppedUnexpectedly;

    public bool IsRunning => Volatile.Read(ref _runningFlag) == 1;
    public bool IsDirectDuplex => _directDuplex;
    public bool IsWasapiInput => _usingWasapiInput;

    public long PaddedFrames => _ring?.PaddedFrames ?? 0;
    public long DroppedFrames => _ring?.DroppedOldest ?? 0;
    public long LostFrames => _ring?.LostNewest ?? 0;
    public int EffectiveSampleRate => _rate;

    public static string[] GetDriverNames() => AsioDevice.GetDriverNames();

    /// <summary>ASIO 드라이버가 허용하는 버퍼 크기 목록.</summary>
    public static List<int> ValidBufferSizes(NAudio.Wave.Asio.AsioDriverCapability caps)
    {
        int min = Math.Max(8, caps.BufferMinSize);
        int max = Math.Max(min, caps.BufferMaxSize);
        var list = new List<int>();
        int g = caps.BufferGranularity;
        if (g > 1)
        {
            for (int v = min; v <= max && list.Count < 32; v += g) list.Add(v);
        }
        else
        {
            int v = 1;
            while (v > 0 && v < min) v <<= 1;
            for (; v > 0 && v <= max && list.Count < 20; v <<= 1) list.Add(v);
        }
        if (list.Count == 0 && caps.BufferPreferredSize > 0) list.Add(caps.BufferPreferredSize);
        return list;
    }

    private static int ResolveBufferSize(NAudio.Wave.Asio.AsioDriverCapability caps, int requested)
    {
        var sizes = ValidBufferSizes(caps);
        if (requested <= 0 || sizes.Count == 0)
            return caps.BufferPreferredSize > 0 ? caps.BufferPreferredSize : (sizes.Count > 0 ? sizes[0] : 256);
        int best = sizes[0];
        foreach (int s in sizes)
            if (Math.Abs(s - requested) < Math.Abs(best - requested)) best = s;
        return best;
    }

    private static MMDevice? ResolveRenderDevice(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var en = new MMDeviceEnumerator();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            if (d.ID == id) return d;
        return null;
    }

    private static MMDevice? ResolveCaptureDevice(string? id)
    {
        var en = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(id))
        {
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                if (d.ID == id) return d;
        }
        try { return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); }
        catch { return en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).FirstOrDefault(); }
    }

    private static WasapiPlayer BuildSharedPlayer(RouterConfig cfg, int latencyMs, bool requireLowLatency)
    {
        var b = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithLatency(latencyMs)
            .WithMmcssThreadPriority("Pro Audio");
        if (requireLowLatency) b.WithLowLatency(true);
        var dev = ResolveRenderDevice(cfg.WasapiDeviceId);
        if (dev != null) b.WithDevice(dev);
        return b.Build();
    }

    private WasapiPlayer InitSharedWithFallback(RouterConfig cfg, WasapiPlayer player, RingSource source)
    {
        try
        {
            player.Init(source);
            return player;
        }
        catch (Exception ex)
        {
            RaiseLog($"공유 모드 초기화 실패({ex.Message}) → 표준 공유 모드({cfg.WasapiLatencyMs}ms)로 재시도");
            player.Dispose();
            var std = BuildSharedPlayer(cfg, Math.Max(8, cfg.WasapiLatencyMs), requireLowLatency: false);
            try
            {
                std.Init(source);
                return std;
            }
            catch (Exception exStd)
            {
                RaiseLog($"표준 공유 모드 초기화 실패({exStd.Message}) → 보수적 50ms로 재시도");
                std.Dispose();
                std = BuildSharedPlayer(cfg, 50, requireLowLatency: false);
                std.Init(source);
                return std;
            }
        }
    }

    // ── 백킹 트랙 (MR) 관리 API ──────────────────────────────────────────

    public BackingTrackState LoadBackingTrack(string wavPath)
    {
        if (!File.Exists(wavPath))
            throw new FileNotFoundException("백킹 트랙 파일을 찾을 수 없습니다.", wavPath);

        int targetSr = _rate > 0 ? _rate : 44100;
        var buf = DecodeBackingBuffer(wavPath, targetSr);
        Volatile.Write(ref _backingPlaying, 0);
        Volatile.Write(ref _backingPos, 0);
        Volatile.Write(ref _backingSeekReq, -1);
        Volatile.Write(ref _backing, buf);
        RaiseLog($"[MR] 백킹 트랙 로드됨: {buf.Name} ({buf.Length / (double)targetSr:F1}초 @ {targetSr}Hz)");
        return GetBackingState();
    }

    public BackingTrackState SetBackingControl(bool? playing = null, bool? loop = null, float? volume = null, double? seekSec = null)
    {
        if (loop.HasValue) Volatile.Write(ref _backingLoop, loop.Value ? 1 : 0);
        if (volume.HasValue) Volatile.Write(ref _backingVolume, Math.Clamp(volume.Value, 0f, 1.5f));
        var buf = Volatile.Read(ref _backing);
        if (seekSec.HasValue && buf != null && buf.SampleRate > 0)
        {
            int frame = Math.Clamp((int)(seekSec.Value * buf.SampleRate), 0, Math.Max(0, buf.Length - 1));
            Volatile.Write(ref _backingSeekReq, frame);
        }
        if (playing.HasValue)
        {
            if (buf == null && playing.Value)
                throw new InvalidOperationException("로드된 백킹 트랙이 없습니다.");
            Volatile.Write(ref _backingPlaying, playing.Value ? 1 : 0);
        }
        return GetBackingState();
    }

    public void UnloadBackingTrack()
    {
        Volatile.Write(ref _backingPlaying, 0);
        Volatile.Write(ref _backing, null);
        Volatile.Write(ref _backingPos, 0);
        Volatile.Write(ref _backingSeekReq, -1);
    }

    public BackingTrackState GetBackingState()
    {
        var buf = Volatile.Read(ref _backing);
        if (buf == null || buf.Length == 0 || buf.SampleRate <= 0)
            return new BackingTrackState(false, false, Volatile.Read(ref _backingLoop) == 1, Volatile.Read(ref _backingVolume), 0, 0, "", "");

        int pos = Math.Clamp(Volatile.Read(ref _backingPos), 0, buf.Length);
        return new BackingTrackState(
            true,
            Volatile.Read(ref _backingPlaying) == 1,
            Volatile.Read(ref _backingLoop) == 1,
            MathF.Round(Volatile.Read(ref _backingVolume), 2),
            Math.Round((double)pos / buf.SampleRate, 1),
            Math.Round((double)buf.Length / buf.SampleRate, 1),
            buf.Name,
            buf.Path);
    }

    private static BackingBuffer DecodeBackingBuffer(string path, int targetSr)
    {
        using var reader = new AudioFileReader(path);
        int srcRate = reader.WaveFormat.SampleRate;
        int ch = Math.Max(1, reader.WaveFormat.Channels);
        int maxFrames = srcRate * 360; // 최대 6분 안전 상한
        var left = new List<float>(Math.Min(srcRate * 45, maxFrames));
        var right = new List<float>(Math.Min(srcRate * 45, maxFrames));
        var chunk = new float[4096 * ch];
        int read;
        while ((read = reader.Read(chunk.AsSpan())) > 0 && left.Count < maxFrames)
        {
            int nFrames = Math.Min(read / ch, maxFrames - left.Count);
            for (int i = 0; i < nFrames; i++)
            {
                float l = chunk[i * ch];
                float r = ch > 1 ? chunk[i * ch + 1] : l;
                left.Add(l);
                right.Add(r);
            }
        }

        float[] arrL = left.ToArray();
        float[] arrR = right.ToArray();
        if (srcRate != targetSr && srcRate > 0 && arrL.Length > 1)
        {
            arrL = SincResampler.Resample(arrL, srcRate, targetSr);
            arrR = SincResampler.Resample(arrR, srcRate, targetSr);
        }
        return new BackingBuffer(arrL, arrR, targetSr, Path.GetFileName(path), path);
    }

    public async Task StartAsync(RouterConfig cfg, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _runningFlag) == 1) throw new InvalidOperationException("이미 실행 중입니다.");
        if (Volatile.Read(ref _stopping) == 1) throw new InvalidOperationException("중지 처리 중입니다. 잠시 후 다시 시도하세요.");

        _stopCts?.Dispose();
        _stopCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token);
        CancellationToken token = linked.Token;

        _directDuplex = false;
        _usingWasapiInput = cfg.InputKind == InputBackendKind.Wasapi;
        Volatile.Write(ref _visPos, 0);
        Volatile.Write(ref _dryVisPos, 0);
        Volatile.Write(ref _dryTotalWritten, 0);

        if (_usingWasapiInput)
        {
            await StartWasapiInputPipelineAsync(cfg, token);
            return;
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            RaiseLog("경고: STA 스레드가 아닙니다. ASIO COM 드라이버 생성이 실패할 수 있습니다.");

        await StartAsioInputPipelineAsync(cfg, token);
    }

    // ── 경로 A: 범용 Windows WASAPI 캡처 입력 파이프라인 (USB 기타 케이블 / 무드라이버 인터페이스 / 라인인 / 마이크) ──
    private async Task StartWasapiInputPipelineAsync(RouterConfig cfg, CancellationToken token)
    {
        WasapiRecorder? wasapiIn = null;
        AsioDevice? output = null;
        WasapiPlayer? wasapi = null;
        bool exclusiveOutAttempted = false;

        try
        {
            var capDev = ResolveCaptureDevice(cfg.WasapiInDeviceId)
                ?? throw new InvalidOperationException("활성화된 Windows 오디오 입력(녹음) 장치를 찾을 수 없습니다.");

            int reqInMs = Math.Max(6, cfg.WasapiLatencyMs > 0 ? cfg.WasapiLatencyMs : 15);
            wasapiIn = CreateAndStartWasapiRecorder(capDev, cfg.WasapiInExclusive, cfg.WasapiInRaw, reqInMs);

            var rawFmt = wasapiIn.WaveFormat;
            var stdFmt = rawFmt is WaveFormatExtensible wfe ? wfe.ToStandardWaveFormat() : rawFmt;
            int rate = Math.Max(8000, stdFmt.SampleRate);
            int outRate = rate;

            _wasapiInChannels = Math.Max(1, stdFmt.Channels);
            _wasapiInBytesPerFrame = Math.Max(2, stdFmt.BlockAlign);
            _wasapiInIsFloat = stdFmt.Encoding == WaveFormatEncoding.IeeeFloat;
            _wasapiInBits = stdFmt.BitsPerSample;

            int startCh = cfg.ChannelMode == 2
                ? (_wasapiInChannels > 1 ? 1 : 0)
                : Math.Clamp(cfg.InputChannelOffset, 0, _wasapiInChannels - 1);
            _wasapiCh0 = startCh;
            _wasapiCh1 = cfg.ChannelMode == 0 && _wasapiInChannels > 1
                ? Math.Min(_wasapiInChannels - 1, startCh + 1)
                : startCh;
            _inSelChannels = cfg.ChannelMode == 0 && _wasapiInChannels > 1 ? 2 : 1;

            _usedInLatencyMs = wasapiIn.LatencyMilliseconds > 0 ? wasapiIn.LatencyMilliseconds : reqInMs;
            _inFrames = Math.Max(64, (int)Math.Round(_usedInLatencyMs * rate / 1000.0));
            int capScratch = Math.Max(4096, _inFrames * 4);
            _scratchL = new float[capScratch];
            _scratchR = new float[capScratch];
            _scratchWarned = false;
            Tone.Prepare(capScratch, rate);
            Recorder.Prepare(rate);

            RaiseLog($"범용 WASAPI 입력 '{capDev.FriendlyName}': {_wasapiInChannels}ch {(stdFmt.Encoding == WaveFormatEncoding.IeeeFloat ? "Float32" : $"PCM{_wasapiInBits}")} @ {rate}Hz (지연 ≈ {_usedInLatencyMs:F1}ms, 저지연={wasapiIn.LowLatencyActive})");

            // WASAPI 입력 시 AsioDirectDuplex는 해당 없으므로 WASAPI 출력으로 안전 전환
            OutputKind outKind = cfg.Kind == OutputKind.AsioDirectDuplex ? OutputKind.Wasapi : cfg.Kind;

            if (outKind == OutputKind.AsioOut)
            {
                output = AsioDevice.Open(cfg.AsioOutDriver ?? throw new InvalidOperationException("ASIO 출력 드라이버가 선택되지 않았습니다."));
                var oc = output.Capabilities;
                outRate = output.IsSampleRateSupported(rate)
                    ? rate
                    : (output.CurrentSampleRate > 0 ? output.CurrentSampleRate : rate);
                if (outRate != rate)
                    RaiseLog($"범용 입력 → ASIO 출력: 입력 {rate}Hz → 출력 {outRate}Hz 실시간 무할당 브리지 활성화");
            }
            else if (cfg.Exclusive)
            {
                exclusiveOutAttempted = true;
                var b = new WasapiPlayerBuilder().WithExclusiveMode().WithLatency(Math.Max(8, cfg.WasapiLatencyMs)).WithMmcssThreadPriority("Pro Audio");
                var dev = ResolveRenderDevice(cfg.WasapiDeviceId);
                if (dev != null) b.WithDevice(dev);
                wasapi = b.Build();
                var f32 = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
                WasapiPlaybackCapability? cap = null;
                try { cap = wasapi.GetPlaybackCapability(f32); } catch { }
                if (cap is { Supported: true })
                {
                    outRate = cap.OutputFormat?.SampleRate ?? rate;
                }
                else
                {
                    var alt = wasapi.GetSupportedExclusiveFormat(f32);
                    if (alt is { SampleRate: > 0 })
                        outRate = alt.SampleRate;
                    else
                    {
                        RaiseLog("WASAPI 출력 단독 포맷 미지원 → 저지연 공유 모드로 대체");
                        wasapi.Dispose();
                        wasapi = null;
                        exclusiveOutAttempted = false;
                    }
                }
            }

            if (outKind == OutputKind.Wasapi && wasapi is null)
            {
                wasapi = BuildSharedPlayer(cfg, Math.Max(10, cfg.WasapiLatencyMs), requireLowLatency: true);
                int mixRate = 0;
                try { mixRate = wasapi.DeviceMixFormat?.SampleRate ?? 0; } catch { }
                if (mixRate > 0 && mixRate != rate)
                {
                    outRate = mixRate;
                    RaiseLog($"범용 입력 {rate}Hz → 출력 {outRate}Hz 실시간 무할당 브리지 활성화");
                }
            }

            _rate = rate;
            EnsureBackingMatchesRate(rate);
            var ring = new RingBuffer(Math.Max(8192, Math.Max(rate, outRate)));
            int targetFrames = Math.Max(128, Math.Max(8, cfg.RingTargetMs) * rate / 1000);
            var source = new RingSource(ring, rate, targetFrames, _inFrames, outRate);
            source.Note += m => RaiseLog(m);

            _source = source;
            _ring = ring; // 이 시점부터 실행 중인 wasapiIn 콜백 패킷이 링에 적재됨
            _inWasapi = wasapiIn;

            wasapiIn.RecordingStopped += (_, e) =>
            {
                if (_runningFlag == 1)
                    RaiseStoppedUnexpected(e.Exception != null ? $"WASAPI 입력 오류: {e.Exception.Message}" : "WASAPI 입력 장치 연결이 해제되었습니다.");
            };

            if (outKind == OutputKind.AsioOut)
            {
                var outDev = output!;
                var oc2 = outDev.Capabilities;
                int outBuf = oc2.BufferMinSize > 0 ? Math.Min(oc2.BufferMinSize, 512) : 256;
                outDev.InitPlayback(new AsioPlaybackOptions
                {
                    Source = source,
                    AutoStopOnEndOfStream = false,
                    BufferSize = outBuf != oc2.BufferPreferredSize ? outBuf : null,
                });
                outDev.Stopped += (_, _) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 출력 정지됨"); };
                outDev.DriverResetRequest += (_, _) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 출력 드라이버 재설정 요청"); };
                await PrerollAsync(Math.Max(targetFrames, outBuf * 3), token);
                outDev.Start();
                _outAsio = outDev;
                _usedOutLatencyMs = outDev.OutputLatencySamples > 0 ? outDev.OutputLatencySamples * 1000.0 / outRate : outBuf * 1000.0 / outRate;
            }
            else
            {
                wasapi ??= BuildSharedPlayer(cfg, Math.Max(10, cfg.WasapiLatencyMs), requireLowLatency: true);
                try
                {
                    wasapi.Init(source);
                }
                catch (Exception ex) when (exclusiveOutAttempted)
                {
                    RaiseLog($"출력 단독 모드 실패({ex.Message}) → 저지연 공유 모드로 대체");
                    wasapi.Dispose();
                    wasapi = InitSharedWithFallback(cfg, BuildSharedPlayer(cfg, Math.Max(10, cfg.WasapiLatencyMs), requireLowLatency: true), source);
                }
                catch (Exception ex)
                {
                    RaiseLog($"저지연 공유 출력 실패({ex.Message}) → 표준 공유 모드로 대체");
                    wasapi.Dispose();
                    wasapi = InitSharedWithFallback(cfg, BuildSharedPlayer(cfg, Math.Max(12, cfg.WasapiLatencyMs), requireLowLatency: false), source);
                }

                int preroll = targetFrames;
                if (wasapi.LatencyMilliseconds > 0) preroll = Math.Max(preroll, wasapi.LatencyMilliseconds * rate / 1000);
                await PrerollAsync(preroll, token);
                wasapi.Play();
                _wasapi = wasapi;
                _usedOutLatencyMs = wasapi.LatencyMilliseconds > 0 ? wasapi.LatencyMilliseconds : cfg.WasapiLatencyMs;
                wasapi.PlaybackStopped += (_, _) => { if (_runningFlag == 1) RaiseStoppedUnexpected("WASAPI 출력 정지됨"); };
            }

            Volatile.Write(ref _runningFlag, 1);
            RaiseState("실행 중 (범용 WASAPI)");
            RaiseLog($"지연 체인(추정): 입력 {_usedInLatencyMs:F1}ms + 링 {cfg.RingTargetMs}ms + 출력 {_usedOutLatencyMs:F1}ms ≈ {_usedInLatencyMs + cfg.RingTargetMs + _usedOutLatencyMs:F1}ms");
        }
        catch
        {
            if (wasapiIn != null && _inWasapi is null)
            {
                try { wasapiIn.StopRecording(); } catch { }
                try { wasapiIn.Dispose(); } catch { }
            }
            Cleanup();
            throw;
        }
    }

    private WasapiRecorder CreateAndStartWasapiRecorder(MMDevice capDev, bool exclusive, bool rawMode, int bufferMs)
    {
        // 1) 단독 입력 모드 요청 시 우선 시도
        if (exclusive)
        {
            WasapiRecorder? exclRec = null;
            try
            {
                var eb = new WasapiRecorderBuilder()
                    .WithDevice(capDev)
                    .WithExclusiveMode()
                    .WithBufferLength(Math.Clamp(bufferMs, 4, 50))
                    .WithMmcssThreadPriority("Pro Audio");
                exclRec = eb.Build();
                exclRec.DataAvailable += OnWasapiCaptured;
                exclRec.StartRecording();
                RaiseLog("WASAPI 입력 단독(Exclusive) 저지연 모드 활성화");
                return exclRec;
            }
            catch (Exception ex)
            {
                RaiseLog($"WASAPI 입력 단독 모드 미지원({ex.Message}) → 저지연 공유 모드로 전환");
                if (exclRec != null)
                {
                    exclRec.DataAvailable -= OnWasapiCaptured;
                    try { exclRec.Dispose(); } catch { }
                }
            }
        }

        // 2) 공유 저지연(IAudioClient3) + Raw 악기 모드(소음 억제/AGC 우회) 우선 시도
        if (rawMode)
        {
            WasapiRecorder? rawRec = null;
            try
            {
                var rb = new WasapiRecorderBuilder()
                    .WithDevice(capDev)
                    .WithSharedMode()
                    .WithLowLatency(false)
                    .WithRawMode()
                    .WithBufferLength(Math.Clamp(bufferMs, 8, 60))
                    .WithMmcssThreadPriority("Pro Audio");
                rawRec = rb.Build();
                rawRec.DataAvailable += OnWasapiCaptured;
                rawRec.StartRecording();
                RaiseLog("WASAPI Raw 순수 악기 입력 활성화 (시스템 소음 억제/AGC 우회)");
                return rawRec;
            }
            catch (Exception ex)
            {
                RaiseLog($"장치 Raw 스트림 미지원({ex.Message}) → 표준 저지연 공유 캡처로 전환");
                if (rawRec != null)
                {
                    rawRec.DataAvailable -= OnWasapiCaptured;
                    try { rawRec.Dispose(); } catch { }
                }
            }
        }

        // 3) 모든 범용 장치 100% 호환 공유 저지연 캡처
        var stdBuilder = new WasapiRecorderBuilder()
            .WithDevice(capDev)
            .WithSharedMode()
            .WithLowLatency(false)
            .WithBufferLength(Math.Clamp(bufferMs, 10, 60))
            .WithMmcssThreadPriority("Pro Audio");
        var rec = stdBuilder.Build();
        rec.DataAvailable += OnWasapiCaptured;
        rec.StartRecording();
        return rec;
    }

    // ── 경로 B: ASIO 입력 파이프라인 (다채널 범용 오디오 인터페이스 / USB ASIO) ──
    private async Task StartAsioInputPipelineAsync(RouterConfig cfg, CancellationToken token)
    {
        AsioDevice? input = null;
        AsioDevice? output = null;
        WasapiPlayer? wasapi = null;
        bool exclusiveAttempted = false;

        try
        {
            // ── 1) 입력 ASIO 장치 열기 ─────────────────────────────
            input = AsioDevice.Open(cfg.InputDriver);
            var caps = input.Capabilities;
            RaiseLog($"입력 ASIO '{caps.DriverName}': 입력 {caps.NbInputChannels}ch / 출력 {caps.NbOutputChannels}ch / 현재 {input.CurrentSampleRate}Hz");
            if (caps.InputChannelInfos != null)
                for (int i = 0; i < Math.Min(caps.NbInputChannels, caps.InputChannelInfos.Length); i++)
                    RaiseLog($"  입력 ch{i + 1} = {caps.InputChannelInfos[i].name}");
            if (caps.NbInputChannels < 1) throw new InvalidOperationException("입력 채널이 없는 ASIO 드라이버입니다.");

            int startCh = cfg.ChannelMode == 2
                ? (caps.NbInputChannels > 1 ? 1 : 0)
                : Math.Clamp(cfg.InputChannelOffset, 0, caps.NbInputChannels - 1);

            int[] inChannels = cfg.ChannelMode == 0 && caps.NbInputChannels > 1
                ? new[] { startCh, Math.Min(caps.NbInputChannels - 1, startCh + 1) }
                : new[] { startCh };
            _inSelChannels = inChannels.Length;

            int bufSize = ResolveBufferSize(caps, cfg.AsioInBufferSize);
            RaiseLog($"입력 채널: {string.Join("+", inChannels.Select(c => $"Ch{c + 1}"))}, 버퍼: {bufSize} 샘플 ({bufSize * 1000.0 / Math.Max(1, input.CurrentSampleRate):F1} ms)");

            int rate = input.CurrentSampleRate;
            int outRate = rate;

            // ── 2) 단일 ASIO 풀듀플렉스(0-Ring 직결) 분기 ──────────
            if (cfg.Kind == OutputKind.AsioDirectDuplex)
            {
                if (caps.NbOutputChannels < 1)
                    throw new InvalidOperationException($"'{caps.DriverName}'에는 출력 채널이 없어 ASIO 직결 모드를 사용할 수 없습니다.");

                int[] outChannels = caps.NbOutputChannels > 1 ? new[] { 0, 1 } : new[] { 0 };
                _rate = rate;
                EnsureBackingMatchesRate(rate);

                var duplexOpts = new AsioDuplexOptions
                {
                    InputChannels = inChannels,
                    OutputChannels = outChannels,
                    SampleRate = null,
                    BufferSize = bufSize != caps.BufferPreferredSize ? bufSize : null,
                    Processor = OnDuplexProcess,
                };
                input.InitDuplex(duplexOpts);
                _inFrames = Math.Max(32, input.FramesPerBuffer);
                int maxScratch = Math.Max(2048, _inFrames * 2);
                _scratchL = new float[maxScratch];
                _scratchR = new float[maxScratch];
                _scratchWarned = false;
                Tone.Prepare(maxScratch, rate);
                Recorder.Prepare(rate);

                _directDuplex = true;
                input.Stopped += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 풀듀플렉스 스트림 정지됨"); };
                input.DriverResetRequest += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 드라이버 재설정 요청 — 장치 연결 확인"); };
                input.Start();
                _in = input;

                Volatile.Write(ref _runningFlag, 1);
                RaiseState("실행 중 (ASIO 직결)");
                var lat = GetLatency()!.Value;
                RaiseLog($"단일 ASIO 풀듀플렉스 직결 활성화: 입력 {lat.InMs:F1}ms + 링 0.0ms + 출력 {lat.OutMs:F1}ms ≈ {lat.TotalMs:F1}ms");
                return;
            }

            // ── 3) WASAPI 또는 제2 ASIO 출력 장치 준비 ──────────────
            if (cfg.Kind == OutputKind.AsioOut)
            {
                output = AsioDevice.Open(cfg.AsioOutDriver ?? throw new InvalidOperationException("ASIO 출력 드라이버가 선택되지 않았습니다."));
                var oc = output.Capabilities;
                RaiseLog($"출력 ASIO '{oc.DriverName}': 출력 {oc.NbOutputChannels}ch / 현재 {output.CurrentSampleRate}Hz");
                if (!output.IsSampleRateSupported(rate))
                {
                    int cand = output.CurrentSampleRate > 0 ? output.CurrentSampleRate : rate;
                    if (input.IsSampleRateSupported(cand))
                    {
                        RaiseLog($"출력 드라이버에 맞춰 입력 레이트를 {cand}Hz로 변경");
                        rate = outRate = cand;
                    }
                    else
                    {
                        outRate = cand;
                        RaiseLog($"제2 ASIO 출력: 입력 {rate}Hz → 출력 {outRate}Hz 실시간 무할당 브리지 활성화");
                    }
                }
            }
            else if (cfg.Exclusive)
            {
                exclusiveAttempted = true;
                var b = new WasapiPlayerBuilder().WithExclusiveMode().WithLatency(cfg.WasapiLatencyMs).WithMmcssThreadPriority("Pro Audio");
                var dev = ResolveRenderDevice(cfg.WasapiDeviceId);
                if (dev != null) b.WithDevice(dev);
                wasapi = b.Build();
                var f32 = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);

                WasapiPlaybackCapability? cap = null;
                try { cap = wasapi.GetPlaybackCapability(f32); } catch { }

                if (cap is { Supported: true })
                {
                    int outSr = cap.OutputFormat?.SampleRate ?? rate;
                    if (outSr != rate)
                    {
                        if (input.IsSampleRateSupported(outSr))
                        {
                            RaiseLog($"단독 모드: 장치가 요구하는 {outSr}Hz로 입력 레이트 변경");
                            rate = outRate = outSr;
                        }
                        else
                        {
                            outRate = outSr;
                            RaiseLog($"단독 모드: 입력 {rate}Hz → 출력 {outRate}Hz 실시간 무할당 브리지 활성화");
                        }
                    }
                    var conv = cap.Conversions is { Count: > 0 } ? string.Join(", ", cap.Conversions) : "변환 없음";
                    RaiseLog($"단독(exclusive) 모드: {cap.OutputFormat} 직결 ({conv})");
                }
                else
                {
                    var alt = wasapi.GetSupportedExclusiveFormat(f32);
                    int altRate = alt?.SampleRate ?? 0;
                    if (alt != null && altRate > 0)
                    {
                        if (altRate != rate)
                        {
                            if (input.IsSampleRateSupported(altRate))
                            {
                                RaiseLog($"단독 모드: 장치가 요구하는 {altRate}Hz로 입력 레이트 변경");
                                rate = outRate = altRate;
                            }
                            else
                            {
                                outRate = altRate;
                                RaiseLog($"단독 모드: 입력 {rate}Hz → 출력 {outRate}Hz 실시간 무할당 브리지 활성화");
                            }
                        }
                        RaiseLog($"단독(exclusive) 모드: {alt.Encoding} {alt.BitsPerSample}bit {altRate}Hz (플레이어 자동 변환)");
                    }
                    else
                    {
                        RaiseLog("단독(exclusive) 포맷 미지원 → 공유 모드(저지연)로 대체");
                        wasapi.Dispose();
                        wasapi = null;
                        exclusiveAttempted = false;
                    }
                }
            }
            else
            {
                wasapi = BuildSharedPlayer(cfg, cfg.WasapiLatencyMs, requireLowLatency: true);
                int mixRate = 0;
                try { mixRate = wasapi.DeviceMixFormat?.SampleRate ?? 0; } catch { }
                if (mixRate > 0 && mixRate != rate)
                {
                    if (input.IsSampleRateSupported(mixRate))
                    {
                        RaiseLog($"공유 모드: 엔진 믹스 레이트 {mixRate}Hz에 입력 레이트를 맞춤(리샘플 없음)");
                        rate = outRate = mixRate;
                    }
                    else
                    {
                        outRate = mixRate;
                        RaiseLog($"공유 모드: 입력 {rate}Hz → 출력 {outRate}Hz 실시간 무할당 브리지 활성화(IAudioClient3 저지연 유지)");
                    }
                }
            }

            // ── 4) 링 버퍼 + 출력 소스 ─────────────────────────────
            _rate = rate;
            EnsureBackingMatchesRate(rate);
            _ring = new RingBuffer(Math.Max(8192, Math.Max(rate, outRate)));
            int targetFrames = Math.Max(64, cfg.RingTargetMs * rate / 1000);
            if (cfg.Kind == OutputKind.AsioOut) targetFrames = Math.Max(targetFrames, bufSize * 3);
            _source = new RingSource(_ring, rate, targetFrames, bufSize, outRate);
            _source.Note += m => RaiseLog(m);

            // ── 5) 입력 초기화 및 시작 ─────────────────────────────
            var rec = new AsioRecordingOptions
            {
                InputChannels = inChannels,
                SampleRate = rate != input.CurrentSampleRate ? rate : null,
                BufferSize = bufSize != caps.BufferPreferredSize ? bufSize : null,
            };
            input.InitRecording(rec);
            _inFrames = Math.Max(64, input.FramesPerBuffer);
            int capScratch = Math.Max(2048, _inFrames * 2);
            _scratchL = new float[capScratch];
            _scratchR = new float[capScratch];
            _scratchWarned = false;
            Tone.Prepare(capScratch, rate);
            Recorder.Prepare(rate);

            input.AudioCaptured += OnCaptured;
            input.Stopped += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("입력 스트림(ASIO) 정지됨"); };
            input.DriverResetRequest += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("입력 드라이버 재설정 요청 — 인터페이스 연결/전원 상태 확인"); };
            input.Start();
            _in = input;
            RaiseLog($"입력 스트림 시작: {_inSelChannels}ch @ {rate}Hz, 버퍼 {input.FramesPerBuffer}fr");

            // ── 6) 출력 초기화 → 프리롤 → 시작 ─────────────────────
            if (cfg.Kind == OutputKind.AsioOut)
            {
                var outDev = output!;
                var oc2 = outDev.Capabilities;
                int outBuf = oc2.BufferMinSize > 0 ? Math.Min(oc2.BufferMinSize, 512) : 256;
                var po = new AsioPlaybackOptions
                {
                    Source = _source!,
                    AutoStopOnEndOfStream = false,
                    BufferSize = outBuf != oc2.BufferPreferredSize ? outBuf : null,
                };
                outDev.InitPlayback(po);
                outDev.Stopped += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 출력 정지됨"); };
                outDev.DriverResetRequest += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 출력 드라이버 재설정 요청"); };

                await PrerollAsync(Math.Max(targetFrames, outBuf * 3), token);
                outDev.Start();
                _outAsio = outDev;
                _usedOutLatencyMs = outDev.OutputLatencySamples > 0 ? outDev.OutputLatencySamples * 1000.0 / rate : outBuf * 1000.0 / rate;
                RaiseLog($"ASIO 출력 시작: 버퍼 {outDev.FramesPerBuffer}fr (출력 지연 ≈ {_usedOutLatencyMs:F1} ms)");
            }
            else
            {
                wasapi ??= BuildSharedPlayer(cfg, cfg.WasapiLatencyMs, requireLowLatency: true);

                try
                {
                    wasapi.Init(_source);
                }
                catch (Exception ex) when (exclusiveAttempted)
                {
                    RaiseLog($"단독 모드 초기화 실패({ex.Message}) → 공유 모드로 대체");
                    exclusiveAttempted = false;
                    wasapi.Dispose();
                    wasapi = BuildSharedPlayer(cfg, cfg.WasapiLatencyMs, requireLowLatency: true);
                    wasapi = InitSharedWithFallback(cfg, wasapi, _source!);
                }
                catch (Exception ex)
                {
                    RaiseLog($"저지연 공유 모드 초기화 실패: {ex.Message}");
                    wasapi.Dispose();
                    wasapi = InitSharedWithFallback(cfg, BuildSharedPlayer(cfg, cfg.WasapiLatencyMs, requireLowLatency: false), _source!);
                }

                if (wasapi.LowLatencyActive) RaiseLog("IAudioClient3 저지연 공유 모드 활성");
                else if (!string.IsNullOrEmpty(wasapi.LowLatencyUnavailableReason)) RaiseLog($"저지연 미적용: {wasapi.LowLatencyUnavailableReason}");

                int preroll = targetFrames;
                if (wasapi.LatencyMilliseconds > 0) preroll = Math.Max(preroll, wasapi.LatencyMilliseconds * rate / 1000);
                await PrerollAsync(preroll, token);
                wasapi.Play();
                _wasapi = wasapi;
                _usedOutLatencyMs = wasapi.LatencyMilliseconds > 0 ? wasapi.LatencyMilliseconds : cfg.WasapiLatencyMs;
                wasapi.PlaybackStopped += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("WASAPI 출력 정지됨"); };
            }

            Volatile.Write(ref _runningFlag, 1);
            RaiseState("실행 중");
            double inMs = _inFrames * 1000.0 / rate;
            RaiseLog($"지연 체인(추정): 입력 {inMs:F1}ms + 링 {cfg.RingTargetMs}ms + 출력 {_usedOutLatencyMs:F1}ms ≈ {inMs + cfg.RingTargetMs + _usedOutLatencyMs:F1}ms");
        }
        catch (Exception)
        {
            Cleanup();
            throw;
        }
    }

    private void EnsureBackingMatchesRate(int rate)
    {
        var cur = Volatile.Read(ref _backing);
        if (cur != null && cur.SampleRate != rate && File.Exists(cur.Path))
        {
            var reloaded = DecodeBackingBuffer(cur.Path, rate);
            Volatile.Write(ref _backing, reloaded);
            Volatile.Write(ref _backingPos, 0);
        }
    }

    private async Task PrerollAsync(int frames, CancellationToken ct)
    {
        var ring = _ring!;
        var sw = Stopwatch.StartNew();
        while (ring.FillFrames < frames && sw.ElapsedMilliseconds < 500)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            await Task.Delay(4, ct).ConfigureAwait(false);
        }
        if (ring.FillFrames < frames)
            RaiseLog("주의: 프리롤 대기 시간 초과 — 오디오 입력 장치 전원/케이블 연결을 확인하세요.");
    }

    /// <summary>단일 ASIO 풀듀플렉스 RT 콜백 (0-Ring 직결). 할당 없음.</summary>
    private void OnDuplexProcess(in AsioProcessBuffers b)
    {
        var sl = _scratchL;
        var sr = _scratchR;
        if (sl is null || sr is null) return;

        int frames = ClampFrameCount(b.Frames, sl.Length);
        if (frames <= 0) return;

        b.GetInput(0).Slice(0, frames).CopyTo(sl);
        if (_inSelChannels > 1 && b.InputChannelCount > 1)
            b.GetInput(1).Slice(0, frames).CopyTo(sr);
        else
            sl.AsSpan(0, frames).CopyTo(sr);

        ProcessInputFrameBlock(sl, sr, frames);

        if (b.OutputChannelCount >= 2)
        {
            sl.AsSpan(0, frames).CopyTo(b.GetOutput(0).Slice(0, frames));
            sr.AsSpan(0, frames).CopyTo(b.GetOutput(1).Slice(0, frames));
        }
        else if (b.OutputChannelCount == 1)
        {
            var dst = b.GetOutput(0);
            for (int i = 0; i < frames; i++)
                dst[i] = 0.5f * (sl[i] + sr[i]);
        }
    }

    /// <summary>ASIO 입력 콜백(RT 스레드). 할당 금지.</summary>
    private void OnCaptured(object? sender, AsioAudioCapturedEventArgs e)
    {
        var ring = _ring;
        var sl = _scratchL;
        var sr = _scratchR;
        if (ring is null || sl is null || sr is null) return;

        int frames = ClampFrameCount(e.Frames, sl.Length);
        if (frames <= 0) return;

        e.GetChannel(0).Slice(0, frames).CopyTo(sl);
        if (_inSelChannels > 1 && e.ChannelCount > 1)
            e.GetChannel(1).Slice(0, frames).CopyTo(sr);
        else
            sl.AsSpan(0, frames).CopyTo(sr);

        ProcessInputFrameBlock(sl, sr, frames);
        ring.Write(frames, sl, sr);
    }

    /// <summary>범용 WASAPI 캡처 콜백(Pro Audio MMCSS 스레드). 할당 0 Bytes.</summary>
    private void OnWasapiCaptured(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var ring = _ring;
        var sl = _scratchL;
        var sr = _scratchR;
        if (ring is null || sl is null || sr is null) return;

        int bpf = _wasapiInBytesPerFrame;
        if (bpf <= 0 || buffer.Length < bpf) return;

        int totalFrames = buffer.Length / bpf;
        int offsetFrames = 0;
        while (offsetFrames < totalFrames)
        {
            int chunkFrames = Math.Min(totalFrames - offsetFrames, sl.Length);
            var slice = buffer.Slice(offsetFrames * bpf, chunkFrames * bpf);

            int decoded = DecodeWasapiPacketToStereo(
                slice,
                flags,
                _wasapiInChannels,
                bpf,
                _wasapiInIsFloat,
                _wasapiInBits,
                _wasapiCh0,
                _wasapiCh1,
                sl.AsSpan(0, chunkFrames),
                sr.AsSpan(0, chunkFrames));

            if (decoded > 0)
            {
                ProcessInputFrameBlock(sl, sr, decoded);
                ring.Write(decoded, sl, sr);
            }
            offsetFrames += chunkFrames;
        }
    }

    /// <summary>
    /// WASAPI 인터리브 바이트 버퍼(Float32 / PCM16 / PCM24 / PCM32)를 힙 할당 0 Bytes로 디인터리브·정규화한다.
    /// </summary>
    internal static int DecodeWasapiPacketToStereo(
        ReadOnlySpan<byte> buffer,
        AudioClientBufferFlags flags,
        int channels,
        int bytesPerFrame,
        bool isFloat,
        int bitsPerSample,
        int ch0,
        int ch1,
        Span<float> dstL,
        Span<float> dstR)
    {
        if (bytesPerFrame <= 0 || channels <= 0) return 0;
        int frames = Math.Min(buffer.Length / bytesPerFrame, Math.Min(dstL.Length, dstR.Length));
        if (frames <= 0) return 0;

        if ((flags & AudioClientBufferFlags.Silent) != 0)
        {
            dstL.Slice(0, frames).Clear();
            dstR.Slice(0, frames).Clear();
            return frames;
        }

        int c0 = Math.Clamp(ch0, 0, channels - 1);
        int c1 = Math.Clamp(ch1, 0, channels - 1);

        if (isFloat && bitsPerSample == 32)
        {
            var fs = MemoryMarshal.Cast<byte, float>(buffer);
            for (int i = 0; i < frames; i++)
            {
                int b = i * channels;
                dstL[i] = fs[b + c0];
                dstR[i] = fs[b + c1];
            }
            return frames;
        }

        if (!isFloat && bitsPerSample == 16)
        {
            const float inv16 = 1f / 32768f;
            var s16 = MemoryMarshal.Cast<byte, short>(buffer);
            for (int i = 0; i < frames; i++)
            {
                int b = i * channels;
                dstL[i] = s16[b + c0] * inv16;
                dstR[i] = s16[b + c1] * inv16;
            }
            return frames;
        }

        if (!isFloat && bitsPerSample == 24)
        {
            const float inv24 = 1f / 8388608f;
            for (int i = 0; i < frames; i++)
            {
                int fBase = i * bytesPerFrame;
                int p0 = fBase + c0 * 3;
                int p1 = fBase + c1 * 3;
                int v0 = ((buffer[p0] << 8) | (buffer[p0 + 1] << 16) | (buffer[p0 + 2] << 24)) >> 8;
                int v1 = ((buffer[p1] << 8) | (buffer[p1 + 1] << 16) | (buffer[p1 + 2] << 24)) >> 8;
                dstL[i] = v0 * inv24;
                dstR[i] = v1 * inv24;
            }
            return frames;
        }

        if (!isFloat && bitsPerSample == 32)
        {
            const float inv32 = 1f / 2147483648f;
            var s32 = MemoryMarshal.Cast<byte, int>(buffer);
            for (int i = 0; i < frames; i++)
            {
                int b = i * channels;
                dstL[i] = s32[b + c0] * inv32;
                dstR[i] = s32[b + c1] * inv32;
            }
            return frames;
        }

        dstL.Slice(0, frames).Clear();
        dstR.Slice(0, frames).Clear();
        return frames;
    }

    private int ClampFrameCount(int incoming, int maxScratch)
    {
        if (incoming <= maxScratch) return incoming;
        if (!_scratchWarned)
        {
            _scratchWarned = true;
            RaiseLog($"주의: 드라이버가 예상({maxScratch})보다 큰 프레임({incoming})을 전달 — 앞부분만 처리");
        }
        return maxScratch;
    }

    private void ProcessInputFrameBlock(float[] sl, float[] sr, int frames)
    {
        // 0) 원클릭 실시간 DI 리앰핑 (활성 시 라이브 입력 대신 로드된 Dry DI 트랙 주입, 할당 0)
        Recorder.InjectReampDryRt(sl.AsSpan(0, frames), sr.AsSpan(0, frames), frames);

        // 0.5) Dual-Stream Dry DI 무할당 캡처 (ToneProcessor 직전)
        Recorder.CaptureDryRt(sl.AsSpan(0, frames), sr.AsSpan(0, frames), frames);

        // 1) 크로매틱 튜너 / 기타 분석용 드라이 입력 스냅샷 + RT 입력 피크 측정
        double rawIn0 = 0, rawIn1 = 0;
        int dp = _dryVisPos;
        for (int i = 0; i < frames; i++)
        {
            float inL = sl[i];
            float inR = sr[i];
            float al = Math.Abs(inL);
            float ar = Math.Abs(inR);
            if (al > rawIn0) rawIn0 = al;
            if (ar > rawIn1) rawIn1 = ar;
            _dryVisRing[dp] = 0.5f * (inL + inR);
            dp = (dp + 1) & (VisCap - 1);
        }
        Volatile.Write(ref _dryVisPos, dp);
        Volatile.Write(ref _dryTotalWritten, _dryTotalWritten + frames);

        if (rawIn0 >= 0.995 || rawIn1 >= 0.995)
            Volatile.Write(ref _inClipTick, Environment.TickCount64);

        // 2) 실시간 신경망 톤 프로세싱 (Bypass일 때도 루퍼는 계속 동작, 할당 0)
        Tone.Process(sl, sr, frames);

        // 2.5) 튜너 무음 모드: 드라이 링 버퍼(_dryVisRing)에는 계속 적재하되 출력은 무음(할당 0)
        if (Volatile.Read(ref _tunerMute) == 1)
        {
            Array.Clear(sl, 0, frames);
            Array.Clear(sr, 0, frames);
        }

        // 2.8) Dual-Stream Post-Tone Stereo Wet 무할당 캡처 (백킹 트랙 믹스 전 순수 기타 톤)
        Recorder.CaptureWetRt(sl.AsSpan(0, frames), sr.AsSpan(0, frames), frames);

        // 3) 비주얼라이저용 포스트 톤 스냅샷 (MR 믹스 전 단계)
        int vp = _visPos;
        for (int i = 0; i < frames; i++)
        {
            _visRing[vp] = 0.5f * (sl[i] + sr[i]);
            vp = (vp + 1) & (VisCap - 1);
        }
        Volatile.Write(ref _visPos, vp);

        // 4) 백킹 트랙(MR) 락프리 믹스
        MixBackingTrack(sl, sr, frames);

        // 5) 스테레오 출력 피크 측정
        double p0 = 0, p1 = 0;
        for (int i = 0; i < frames; i++)
        {
            float v = Math.Abs(sl[i]);
            if (v > p0) p0 = v;
            float w = Math.Abs(sr[i]);
            if (w > p1) p1 = w;
        }
        Interlocked.Exchange(ref _pkL, BitConverter.DoubleToInt64Bits(p0));
        Interlocked.Exchange(ref _pkR, BitConverter.DoubleToInt64Bits(p1));
        if (p0 >= 0.995 || p1 >= 0.995)
            Volatile.Write(ref _clipTick, Environment.TickCount64);
    }

    private void MixBackingTrack(float[] sl, float[] sr, int frames)
    {
        var buf = Volatile.Read(ref _backing);
        if (buf is null || buf.Length == 0 || Volatile.Read(ref _backingPlaying) == 0)
            return;

        int seek = Interlocked.Exchange(ref _backingSeekReq, -1);
        int pos = seek >= 0 ? seek : Volatile.Read(ref _backingPos);
        bool loop = Volatile.Read(ref _backingLoop) == 1;
        float gain = Volatile.Read(ref _backingVolume);
        int len = buf.Length;
        var bL = buf.Left;
        var bR = buf.Right;

        for (int i = 0; i < frames; i++)
        {
            if (pos >= len)
            {
                if (!loop)
                {
                    Volatile.Write(ref _backingPlaying, 0);
                    pos = 0;
                    break;
                }
                pos = 0;
            }
            sl[i] = Math.Clamp(sl[i] + bL[pos] * gain, -0.99f, 0.99f);
            sr[i] = Math.Clamp(sr[i] + bR[pos] * gain, -0.99f, 0.99f);
            pos++;
        }
        Volatile.Write(ref _backingPos, pos);
    }

    public void Stop(string reason = "사용자 중지")
    {
        if (Volatile.Read(ref _runningFlag) == 0 && _in is null && _inWasapi is null && _wasapi is null && _outAsio is null) return;
        Volatile.Write(ref _stopping, 1);
        try { _stopCts?.Cancel(); } catch { }
        Cleanup();
        Volatile.Write(ref _stopping, 0);
        RaiseState($"중지됨 — {reason}");
        RaiseLog($"중지: {reason}");
    }

    private void Cleanup()
    {
        var input = _in;
        var inputWasapi = _inWasapi;
        var output = _outAsio;
        var w = _wasapi;
        _in = null;
        _inWasapi = null;
        _outAsio = null;
        _wasapi = null;
        _ring = null;
        _source = null;
        _scratchL = null;
        _scratchR = null;
        _directDuplex = false;
        _usingWasapiInput = false;
        Array.Clear(_visRing);
        Array.Clear(_dryVisRing);
        Volatile.Write(ref _visPos, 0);
        Volatile.Write(ref _dryVisPos, 0);
        Volatile.Write(ref _dryTotalWritten, 0);
        Volatile.Write(ref _tunerMute, 0);
        Volatile.Write(ref _clipTick, 0);
        Volatile.Write(ref _inClipTick, 0);
        Volatile.Write(ref _runningFlag, 0);
        if (Recorder.IsRecording)
        {
            try { Recorder.StopRecording(); } catch { }
        }

        if (w != null)
        {
            try { w.Stop(); } catch { }
            try { w.Dispose(); } catch { }
        }
        if (output != null)
        {
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { }
        }
        if (inputWasapi != null)
        {
            try { inputWasapi.DataAvailable -= OnWasapiCaptured; } catch { }
            try { inputWasapi.StopRecording(); } catch { }
            try { inputWasapi.Dispose(); } catch { }
        }
        if (input != null)
        {
            try { input.AudioCaptured -= OnCaptured; } catch { }
            try { input.Stop(); } catch { }
            try { input.Dispose(); } catch { }
        }
    }

    public (float l, float r, bool clip) TakePeaks()
    {
        var (_, _, _, outL, outR, outClip) = TakeIoPeaks();
        return (outL, outR, outClip);
    }

    public (float inL, float inR, bool inClip, float outL, float outR, bool outClip) TakeIoPeaks()
    {
        Tone.TakeRtMeters(out float inL, out float inR, out bool toneInClip, out bool toneOutClip);
        long now = Environment.TickCount64;
        if (toneInClip) Volatile.Write(ref _inClipTick, now);
        if (toneOutClip) Volatile.Write(ref _clipTick, now);

        long l = Interlocked.Exchange(ref _pkL, 0);
        long r = Interlocked.Exchange(ref _pkR, 0);
        float outL = (float)BitConverter.Int64BitsToDouble(l);
        float outR = (float)BitConverter.Int64BitsToDouble(r);
        if (outL >= 0.99f || outR >= 0.99f) Volatile.Write(ref _clipTick, now);

        long inTick = Volatile.Read(ref _inClipTick);
        long outTick = Volatile.Read(ref _clipTick);
        bool inClip = inTick > 0 && now - inTick < 600;
        bool outClip = outTick > 0 && now - outTick < 600;
        return (inL, inR, inClip, outL, outR, outClip);
    }

    public LatencyInfo? GetLatency()
    {
        if (_rate <= 0) return null;
        double inMs = _usingWasapiInput
            ? (_inWasapi != null && _inWasapi.CurrentLatency > TimeSpan.Zero ? _inWasapi.CurrentLatency.TotalMilliseconds : _usedInLatencyMs)
            : (_in != null && _in.InputLatencySamples > 0 ? _in.InputLatencySamples * 1000.0 / _rate : _inFrames * 1000.0 / _rate);

        if (_directDuplex)
        {
            double outD = (_in != null && _in.OutputLatencySamples > 0 ? _in.OutputLatencySamples : _inFrames) * 1000.0 / _rate;
            return new LatencyInfo(inMs, 0.0, outD, inMs + outD);
        }
        if (_ring is null) return null;
        double ringMs = _ring.FillFrames * 1000.0 / _rate;
        double outMs;
        if (_outAsio != null && _outAsio.OutputLatencySamples > 0)
            outMs = _outAsio.OutputLatencySamples * 1000.0 / _rate;
        else if (_wasapi != null && _wasapi.CurrentLatency > TimeSpan.Zero)
            outMs = _wasapi.CurrentLatency.TotalMilliseconds;
        else
            outMs = _usedOutLatencyMs;
        return new LatencyInfo(inMs, ringMs, outMs, inMs + ringMs + outMs);
    }

    private void RaiseLog(string m) => Log?.Invoke(m);
    private void RaiseState(string s) => StateChanged?.Invoke(s);
    private void RaiseStoppedUnexpected(string r) => StoppedUnexpectedly?.Invoke(r);

    /// <summary>UI 스레드에서 포스트 톤 최신 N 샘플을 락 없이 읽어온다(비주얼라이저용).</summary>
    public void CopyRecentSamples(Span<float> dst)
    {
        int n = Math.Min(dst.Length, VisCap);
        int vp = Volatile.Read(ref _visPos);
        int start = (vp - n + VisCap) & (VisCap - 1);
        for (int i = 0; i < n; i++)
            dst[i] = _visRing[(start + i) & (VisCap - 1)];
    }

    /// <summary>UI 스레드에서 드라이 입력 최신 N 샘플을 락 없이 읽어온다(크로매틱 튜너용).</summary>
    public void CopyRecentDrySamples(Span<float> dst)
    {
        int n = Math.Min(dst.Length, VisCap);
        int dp = Volatile.Read(ref _dryVisPos);
        int start = (dp - n + VisCap) & (VisCap - 1);
        for (int i = 0; i < n; i++)
            dst[i] = _dryVisRing[(start + i) & (VisCap - 1)];
    }

    /// <summary>UI 스레드에서 이전 커서 이후 도착한 드라이 기타 샘플을 끊김 없이 읽어온다(기타 분석기용).</summary>
    public int CopyDrySamplesSince(ref long readTotal, Span<float> dst)
    {
        long written = Volatile.Read(ref _dryTotalWritten);
        if (readTotal < 0 || written < readTotal)
        {
            readTotal = written;
            return 0;
        }
        long avail = written - readTotal;
        if (avail <= 0) return 0;
        if (avail > VisCap - 128)
        {
            readTotal = written - (VisCap - 128);
            avail = VisCap - 128;
        }
        int count = (int)Math.Min(avail, dst.Length);
        for (int i = 0; i < count; i++)
            dst[i] = _dryVisRing[(int)((readTotal + i) & (VisCap - 1))];
        readTotal += count;
        return count;
    }
}
