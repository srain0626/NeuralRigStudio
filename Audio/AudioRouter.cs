using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace NeuralRigStudio.Audio;

public enum OutputKind
{
    Wasapi,
    AsioOut,
    AsioDirectDuplex,
}

public sealed class RouterConfig
{
    public required string InputDriver;
    public int ChannelMode;              // 0: 스테레오(L+R), 1: 모노 L, 2: 모노 R
    public int AsioInBufferSize;         // 0 = 드라이버 선호 크기
    public OutputKind Kind = OutputKind.Wasapi;
    public bool Exclusive;               // WASAPI 단독 모드
    public string? WasapiDeviceId;       // null = 시스템 기본 장치
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
/// ASIO 입력 → (단일 ASIO 풀듀플렉스 직결 OR Lock-free 링 버퍼 → WASAPI / 제2 ASIO) 초저지연 오디오 라우터.
/// + Lock-free MR 백킹 트랙 믹서 + 50fps 비주얼라이저 / YIN 크로매틱 튜너 스냅샷 버퍼.
/// </summary>
public sealed class AudioRouter
{
    private AsioDevice? _in;
    private AsioDevice? _outAsio;
    private WasapiPlayer? _wasapi;
    private RingBuffer? _ring;
    private RingSource? _source;
    private float[]? _scratchL;
    private float[]? _scratchR;
    private CancellationTokenSource? _stopCts;

    private int _rate;
    private int _inSelChannels;      // 1 or 2
    private int _inFrames;           // 입력 버퍼 프레임
    private double _usedOutLatencyMs;
    private bool _directDuplex;
    private int _runningFlag;
    private int _stopping;

    private long _pkL;               // double 비트 저장
    private long _pkR;
    private long _clipTick;
    private bool _scratchWarned;

    // ── 실시간 AVX2 WaveNet / 4x WhiteBox 톤 프로세서 ──
    public ToneProcessor Tone { get; } = new();

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

    private static MMDevice? ResolveDevice(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var en = new MMDeviceEnumerator();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            if (d.ID == id) return d;
        return null;
    }

    private static WasapiPlayer BuildSharedPlayer(RouterConfig cfg, int latencyMs, bool requireLowLatency)
    {
        var b = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithLatency(latencyMs)
            .WithMmcssThreadPriority("Pro Audio");
        if (requireLowLatency) b.WithLowLatency(true);
        var dev = ResolveDevice(cfg.WasapiDeviceId);
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
            arrL = ResampleLinear(arrL, srcRate, targetSr);
            arrR = ResampleLinear(arrR, srcRate, targetSr);
        }
        return new BackingBuffer(arrL, arrR, targetSr, Path.GetFileName(path), path);
    }

    private static float[] ResampleLinear(float[] src, int srcRate, int dstRate)
    {
        double ratio = (double)srcRate / dstRate;
        int outLen = Math.Max(16, (int)Math.Round(src.Length / ratio));
        var dst = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double pos = i * ratio;
            int idx = (int)pos;
            float frac = (float)(pos - idx);
            float a = src[Math.Min(idx, src.Length - 1)];
            float b = src[Math.Min(idx + 1, src.Length - 1)];
            dst[i] = a + (b - a) * frac;
        }
        return dst;
    }

    public async Task StartAsync(RouterConfig cfg, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _runningFlag) == 1) throw new InvalidOperationException("이미 실행 중입니다.");
        if (Volatile.Read(ref _stopping) == 1) throw new InvalidOperationException("중지 처리 중입니다. 잠시 후 다시 시도하세요.");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            RaiseLog("경고: STA 스레드가 아닙니다. ASIO COM 드라이버 생성이 실패할 수 있습니다.");

        _stopCts?.Dispose();
        _stopCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token);
        CancellationToken token = linked.Token;

        AsioDevice? input = null;
        AsioDevice? output = null;
        WasapiPlayer? wasapi = null;
        bool exclusiveAttempted = false;
        _directDuplex = false;
        Volatile.Write(ref _visPos, 0);
        Volatile.Write(ref _dryVisPos, 0);
        Volatile.Write(ref _dryTotalWritten, 0);

        try
        {
            // ── 1) 입력 ASIO 장치 열기 ─────────────────────────────
            input = AsioDevice.Open(cfg.InputDriver);
            var caps = input.Capabilities;
            RaiseLog($"입력 ASIO '{caps.DriverName}': 입력 {caps.NbInputChannels}ch / 출력 {caps.NbOutputChannels}ch / 현재 {input.CurrentSampleRate}Hz");
            if (caps.InputChannelInfos != null)
                for (int i = 0; i < Math.Min(caps.NbInputChannels, caps.InputChannelInfos.Length); i++)
                    RaiseLog($"  입력 ch{i} = {caps.InputChannelInfos[i].name}");
            if (caps.NbInputChannels < 1) throw new InvalidOperationException("입력 채널이 없는 ASIO 드라이버입니다.");

            int[] inChannels = cfg.ChannelMode switch
            {
                1 => new[] { 0 },
                2 => new[] { caps.NbInputChannels > 1 ? 1 : 0 },
                _ => caps.NbInputChannels > 1 ? new[] { 0, 1 } : new[] { 0 },
            };
            _inSelChannels = inChannels.Length;

            int bufSize = ResolveBufferSize(caps, cfg.AsioInBufferSize);
            RaiseLog($"입력 버퍼: {bufSize} 샘플 ({bufSize * 1000.0 / Math.Max(1, input.CurrentSampleRate):F1} ms)");

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

                _directDuplex = true;
                input.Stopped += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 풀듀플렉스 스트림 정지됨"); };
                input.DriverResetRequest += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("ASIO 드라이버 재설정 요청 — 페달 연결 확인"); };
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
                var dev = ResolveDevice(cfg.WasapiDeviceId);
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

            input.AudioCaptured += OnCaptured;
            input.Stopped += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("입력 스트림(ASIO) 정지됨"); };
            input.DriverResetRequest += (s, e) => { if (_runningFlag == 1) RaiseStoppedUnexpected("입력 드라이버 재설정 요청 — 페달 연결/전원 상태 확인"); };
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
            RaiseLog("주의: 프리롤 대기 시간 초과 — 페달 전원/연결/기타 입력을 확인하세요.");
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
        // 1) YIN 크로매틱 튜너 / 기타 분석용 드라이 입력 스냅샷
        int dp = _dryVisPos;
        for (int i = 0; i < frames; i++)
        {
            _dryVisRing[dp] = 0.5f * (sl[i] + sr[i]);
            dp = (dp + 1) & (VisCap - 1);
        }
        Volatile.Write(ref _dryVisPos, dp);
        Volatile.Write(ref _dryTotalWritten, _dryTotalWritten + frames);

        // 2) 실시간 신경망 톤 프로세싱 (Bypass일 때는 즉시 리턴, 할당 0)
        Tone.Process(sl, sr, frames);

        // 2.5) 튜너 무음 모드: 드라이 링 버퍼(_dryVisRing)에는 계속 적재하되 출력은 무음(할당 0)
        if (Volatile.Read(ref _tunerMute) == 1)
        {
            Array.Clear(sl, 0, frames);
            Array.Clear(sr, 0, frames);
        }

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
        if (p0 >= 0.997 || p1 >= 0.997)
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
        if (Volatile.Read(ref _runningFlag) == 0 && _in is null && _wasapi is null && _outAsio is null) return;
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
        var output = _outAsio;
        var w = _wasapi;
        _in = null;
        _outAsio = null;
        _wasapi = null;
        _ring = null;
        _source = null;
        _scratchL = null;
        _scratchR = null;
        _directDuplex = false;
        Array.Clear(_visRing);
        Array.Clear(_dryVisRing);
        Volatile.Write(ref _visPos, 0);
        Volatile.Write(ref _dryVisPos, 0);
        Volatile.Write(ref _dryTotalWritten, 0);
        Volatile.Write(ref _tunerMute, 0);
        Volatile.Write(ref _runningFlag, 0);

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
        if (input != null)
        {
            try { input.AudioCaptured -= OnCaptured; } catch { }
            try { input.Stop(); } catch { }
            try { input.Dispose(); } catch { }
        }
    }

    public (float l, float r, bool clip) TakePeaks()
    {
        long l = Interlocked.Exchange(ref _pkL, 0);
        long r = Interlocked.Exchange(ref _pkR, 0);
        bool clip = Environment.TickCount64 - Volatile.Read(ref _clipTick) < 500;
        return ((float)BitConverter.Int64BitsToDouble(l), (float)BitConverter.Int64BitsToDouble(r), clip);
    }

    public LatencyInfo? GetLatency()
    {
        if (_rate <= 0) return null;
        double inMs = (_in != null && _in.InputLatencySamples > 0 ? _in.InputLatencySamples : _inFrames) * 1000.0 / _rate;
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

    /// <summary>UI 스레드에서 드라이 입력 최신 N 샘플을 락 없이 읽어온다(YIN 크로매틱 튜너용).</summary>
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

