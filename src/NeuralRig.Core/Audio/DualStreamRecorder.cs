using NAudio.Wave;

namespace NeuralRigStudio.Audio;

public sealed record RecorderReampSnapshotDto(
    bool Recording,
    double RecordedSec,
    long DroppedFrames,
    string LastDryPath,
    string LastWetPath,
    string LastDryName,
    string RecordingsDir,
    bool ReampAvailable,
    bool ReampEnabled,
    bool ReampPlaying,
    bool ReampLoop,
    double ReampPositionSec,
    double ReampDurationSec,
    string ReampName,
    string ReampPath);

internal sealed record ReampDryBuffer(
    float[] Left,
    float[] Right,
    int SampleRate,
    string Name,
    string Path)
{
    public int Length => Left.Length;
}

/// <summary>
/// Dual-Stream 레코더 (Dry DI + Post-Tone Stereo Wet 동시 무할당 캡처/WAV 저장)
/// + 원클릭 실시간 DI 리앰핑(Live DI Re-amping) 엔진 (Milestone 4 / v1.5).
/// </summary>
public sealed class DualStreamRecorder : IDisposable
{
    private const int WriterChunkFrames = 4096;
    private const int MaxInMemoryReampSeconds = 600;

    private readonly object _ctrlLock = new();

    private RingBuffer? _dryRing;
    private RingBuffer? _wetRing;
    private int _sampleRate = 48000;

    private int _recording;
    private volatile bool _writerStopRequested;
    private Task? _writerTask;
    private long _dryCapturedFrames;
    private long _wetCapturedFrames;

    private string _recordingsDir = Path.Combine(AppContext.BaseDirectory, "Recordings");
    private string _lastDryPath = "";
    private string _lastWetPath = "";
    private string _lastDryName = "";

    // 원클릭 실시간 리앰핑 상태 (RT 스레드 무할당 읽기)
    private ReampDryBuffer? _reampBuf;
    private int _reampEnabled;
    private int _reampPlaying;
    private int _reampLoop = 1;
    private int _reampPos;
    private int _reampSeekReq = -1;

    public bool IsRecording => Volatile.Read(ref _recording) == 1;
    public bool IsReampEnabled => Volatile.Read(ref _reampEnabled) == 1;
    public bool IsReampPlaying => Volatile.Read(ref _reampEnabled) == 1 && Volatile.Read(ref _reampPlaying) == 1;
    public string RecordingsDirectory => Volatile.Read(ref _recordingsDir);
    public string LastDryPath => Volatile.Read(ref _lastDryPath);
    public string LastWetPath => Volatile.Read(ref _lastWetPath);

    /// <summary>스트림 시작 전 사전 할당 (RT 힙 할당 0 B).</summary>
    public void Prepare(int sampleRate, int ringSeconds = 8)
    {
        int sr = Math.Max(8000, sampleRate);
        lock (_ctrlLock)
        {
            if (_recording == 1)
                return;

            _sampleRate = sr;
            int minCap = sr * Math.Clamp(ringSeconds, 4, 20);
            if (_dryRing is null || _dryRing.CapacityFrames < minCap)
                _dryRing = new RingBuffer(minCap);
            else
                _dryRing.Reset();

            if (_wetRing is null || _wetRing.CapacityFrames < minCap)
                _wetRing = new RingBuffer(minCap);
            else
                _wetRing.Reset();

            // 리앰프 DI 버퍼가 로드되어 있고 샘플레이트가 달라졌다면 피치 유지를 위해 리샘플링
            var cur = Volatile.Read(ref _reampBuf);
            if (cur != null && cur.SampleRate != sr && cur.SampleRate > 0 && cur.Length > 1)
            {
                var rL = SincResampler.Resample(cur.Left, cur.SampleRate, sr);
                var rR = ReferenceEquals(cur.Left, cur.Right)
                    ? rL
                    : SincResampler.Resample(cur.Right, cur.SampleRate, sr);
                Volatile.Write(ref _reampBuf, new ReampDryBuffer(rL, rR, sr, cur.Name, cur.Path));
                Volatile.Write(ref _reampPos, 0);
            }
        }
    }

    /// <summary>
    /// RT 스레드 최상단(Tone.Process 직전): 리앰핑 활성 시 라이브 기타 입력 대신 Dry DI 루프를 주입 (할당 0 B).
    /// </summary>
    public bool InjectReampDryRt(Span<float> sl, Span<float> sr, int frames)
    {
        if (frames <= 0) return false;
        if (Volatile.Read(ref _reampEnabled) == 0 || Volatile.Read(ref _reampPlaying) == 0)
            return false;

        var buf = Volatile.Read(ref _reampBuf);
        if (buf is null || buf.Length <= 0)
            return false;

        int seek = Interlocked.Exchange(ref _reampSeekReq, -1);
        int pos = seek >= 0 ? Math.Clamp(seek, 0, buf.Length - 1) : Volatile.Read(ref _reampPos);
        bool loop = Volatile.Read(ref _reampLoop) == 1;
        int len = buf.Length;
        var bL = buf.Left;
        var bR = buf.Right;

        for (int i = 0; i < frames; i++)
        {
            if (pos >= len)
            {
                if (!loop)
                {
                    Volatile.Write(ref _reampPlaying, 0);
                    sl.Slice(i, frames - i).Clear();
                    sr.Slice(i, frames - i).Clear();
                    pos = 0;
                    break;
                }
                pos = 0;
            }
            sl[i] = bL[pos];
            sr[i] = bR[pos];
            pos++;
        }

        Volatile.Write(ref _reampPos, pos);
        return true;
    }

    /// <summary>RT 스레드(Tone.Process 직전): Dry DI 스트림을 락프리 링 버퍼에 적재 (할당 0 B).</summary>
    public void CaptureDryRt(ReadOnlySpan<float> dryL, ReadOnlySpan<float> dryR, int frames)
    {
        if (frames <= 0 || Volatile.Read(ref _recording) == 0) return;
        var ring = _dryRing;
        if (ring is null) return;
        ring.Write(frames, dryL, dryR);
        Interlocked.Add(ref _dryCapturedFrames, frames);
    }

    /// <summary>RT 스레드(Tone.Process 직후 / MR 믹스 직전): Stereo Wet 스트림을 락프리 링 버퍼에 적재 (할당 0 B).</summary>
    public void CaptureWetRt(ReadOnlySpan<float> wetL, ReadOnlySpan<float> wetR, int frames)
    {
        if (frames <= 0 || Volatile.Read(ref _recording) == 0) return;
        var ring = _wetRing;
        if (ring is null) return;
        ring.Write(frames, wetL, wetR);
        Interlocked.Add(ref _wetCapturedFrames, frames);
    }

    public RecorderReampSnapshotDto StartRecording(string? outputDirectory = null, string? sessionName = null)
    {
        lock (_ctrlLock)
        {
            if (_recording == 1)
                return GetSnapshot();

            if (_dryRing is null || _wetRing is null)
                Prepare(_sampleRate);

            string dir = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "Recordings")
                : outputDirectory.Trim();
            Directory.CreateDirectory(dir);
            Volatile.Write(ref _recordingsDir, dir);

            string safeBase = SanitizeFileName(
                string.IsNullOrWhiteSpace(sessionName)
                    ? $"NeuralRig_{DateTime.Now:yyyyMMdd_HHmmss}"
                    : sessionName.Trim());
            string dryPath = Path.Combine(dir, $"{safeBase}_DryDI.wav");
            string wetPath = Path.Combine(dir, $"{safeBase}_WetStereo.wav");

            _dryRing!.Reset();
            _wetRing!.Reset();
            Interlocked.Exchange(ref _dryCapturedFrames, 0);
            Interlocked.Exchange(ref _wetCapturedFrames, 0);

            int sr = _sampleRate;
            var memDryL = new List<float>(Math.Min(sr * 60, sr * MaxInMemoryReampSeconds));
            var memDryR = new List<float>(Math.Min(sr * 60, sr * MaxInMemoryReampSeconds));
            int maxMemFrames = sr * MaxInMemoryReampSeconds;

            _writerStopRequested = false;
            Volatile.Write(ref _recording, 1);

            _writerTask = Task.Run(() =>
            {
                DrainRingsToWavLoop(sr, dryPath, wetPath, memDryL, memDryR, maxMemFrames);
            });

            return GetSnapshot();
        }
    }

    public RecorderReampSnapshotDto StopRecording()
    {
        lock (_ctrlLock)
        {
            if (Volatile.Read(ref _recording) == 0 && _writerTask is null)
                return GetSnapshot();

            Volatile.Write(ref _recording, 0);
            // 진행 중인 1개 RT 콜백이 링에 쓰기를 마무리할 수 있도록 짧은 유예 후 드레인 종료 신호
            Thread.Sleep(5);
            _writerStopRequested = true;

            try
            {
                _writerTask?.Wait(5000);
            }
            catch
            {
            }
            _writerTask = null;

            return GetSnapshot();
        }
    }

    private void DrainRingsToWavLoop(
        int sr,
        string dryPath,
        string wetPath,
        List<float> memDryL,
        List<float> memDryR,
        int maxMemFrames)
    {
        var dryRing = _dryRing!;
        var wetRing = _wetRing!;
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(sr, 2);

        using var dryWriter = new WaveFileWriter(dryPath, fmt);
        using var wetWriter = new WaveFileWriter(wetPath, fmt);

        var scratch = new float[WriterChunkFrames * 2];

        while (true)
        {
            bool didWork = false;

            int gotDry = dryRing.ReadAvailable(scratch, WriterChunkFrames);
            if (gotDry > 0)
            {
                dryWriter.WriteSamples(scratch, 0, gotDry * 2);
                int space = maxMemFrames - memDryL.Count;
                int addN = Math.Min(gotDry, space);
                for (int i = 0; i < addN; i++)
                {
                    memDryL.Add(scratch[i * 2]);
                    memDryR.Add(scratch[i * 2 + 1]);
                }
                didWork = true;
            }

            int gotWet = wetRing.ReadAvailable(scratch, WriterChunkFrames);
            if (gotWet > 0)
            {
                wetWriter.WriteSamples(scratch, 0, gotWet * 2);
                didWork = true;
            }

            if (didWork) continue;

            if (_writerStopRequested && dryRing.FillFrames == 0 && wetRing.FillFrames == 0)
                break;

            Thread.Sleep(4);
        }

        dryWriter.Flush();
        wetWriter.Flush();

        string dryName = Path.GetFileName(dryPath);
        Volatile.Write(ref _lastDryPath, dryPath);
        Volatile.Write(ref _lastWetPath, wetPath);
        Volatile.Write(ref _lastDryName, dryName);

        if (memDryL.Count > 0)
        {
            var buf = new ReampDryBuffer(memDryL.ToArray(), memDryR.ToArray(), sr, dryName, dryPath);
            Volatile.Write(ref _reampBuf, buf);
            Volatile.Write(ref _reampPos, 0);
            Volatile.Write(ref _reampSeekReq, -1);
        }
    }

    public RecorderReampSnapshotDto LoadReampDryFile(string wavPath)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
            throw new FileNotFoundException("리앰핑할 Dry DI WAV 파일을 찾을 수 없습니다.", wavPath);

        using var reader = new AudioFileReader(wavPath);
        int srcRate = reader.WaveFormat.SampleRate;
        int ch = Math.Max(1, reader.WaveFormat.Channels);
        int maxFrames = srcRate * MaxInMemoryReampSeconds;
        var left = new List<float>(Math.Min(srcRate * 45, maxFrames));
        var right = new List<float>(Math.Min(srcRate * 45, maxFrames));
        var chunk = new float[4096 * ch];
        int read;
        while ((read = reader.Read(chunk.AsSpan())) > 0 && left.Count < maxFrames)
        {
            int n = Math.Min(read / ch, maxFrames - left.Count);
            for (int i = 0; i < n; i++)
            {
                float l = chunk[i * ch];
                float r = ch > 1 ? chunk[i * ch + 1] : l;
                left.Add(l);
                right.Add(r);
            }
        }
        if (left.Count < 8)
            throw new InvalidOperationException("DI 오디오 파일이 비어 있거나 너무 짧습니다.");

        float[] arrL = left.ToArray();
        float[] arrR = right.ToArray();
        int targetSr = Math.Max(8000, _sampleRate);
        if (srcRate != targetSr && srcRate > 0)
        {
            arrL = SincResampler.Resample(arrL, srcRate, targetSr);
            arrR = SincResampler.Resample(arrR, srcRate, targetSr);
        }

        var buf = new ReampDryBuffer(arrL, arrR, targetSr, Path.GetFileName(wavPath), wavPath);
        Volatile.Write(ref _reampBuf, buf);
        Volatile.Write(ref _reampPos, 0);
        Volatile.Write(ref _reampSeekReq, -1);
        Volatile.Write(ref _lastDryPath, wavPath);
        Volatile.Write(ref _lastDryName, buf.Name);

        return GetSnapshot();
    }

    public RecorderReampSnapshotDto SetReampState(bool? enabled = null, bool? playing = null, bool? loop = null, double? seekSec = null)
    {
        var buf = Volatile.Read(ref _reampBuf);
        if (loop.HasValue)
            Volatile.Write(ref _reampLoop, loop.Value ? 1 : 0);

        if (seekSec.HasValue && buf != null && buf.SampleRate > 0 && buf.Length > 0)
        {
            int frame = Math.Clamp((int)(seekSec.Value * buf.SampleRate), 0, buf.Length - 1);
            Volatile.Write(ref _reampSeekReq, frame);
            Volatile.Write(ref _reampPos, frame);
        }

        if (enabled.HasValue)
        {
            if (enabled.Value && (buf is null || buf.Length <= 0))
                throw new InvalidOperationException("리앰핑할 Dry DI 트랙이 없습니다. 먼저 Dual-Stream 녹음을 하거나 DI WAV 파일을 불러오세요.");
            Volatile.Write(ref _reampEnabled, enabled.Value ? 1 : 0);
            if (!enabled.Value)
                Volatile.Write(ref _reampPlaying, 0);
        }

        if (playing.HasValue)
        {
            if (playing.Value && (buf is null || buf.Length <= 0))
                throw new InvalidOperationException("재생할 Dry DI 트랙이 없습니다.");
            Volatile.Write(ref _reampPlaying, playing.Value ? 1 : 0);
            if (playing.Value)
                Volatile.Write(ref _reampEnabled, 1);
        }

        return GetSnapshot();
    }

    public RecorderReampSnapshotDto GetSnapshot()
    {
        bool rec = IsRecording;
        int sr = Math.Max(8000, _sampleRate);
        long recFrames = Math.Max(Interlocked.Read(ref _dryCapturedFrames), Interlocked.Read(ref _wetCapturedFrames));
        long lost = (_dryRing?.LostNewest ?? 0) + (_wetRing?.LostNewest ?? 0);

        var buf = Volatile.Read(ref _reampBuf);
        int pos = buf != null && buf.Length > 0 ? Math.Clamp(Volatile.Read(ref _reampPos), 0, buf.Length) : 0;

        return new RecorderReampSnapshotDto(
            rec,
            rec ? Math.Round((double)recFrames / sr, 2) : 0.0,
            lost,
            LastDryPath,
            LastWetPath,
            Volatile.Read(ref _lastDryName),
            RecordingsDirectory,
            buf != null && buf.Length > 0,
            Volatile.Read(ref _reampEnabled) == 1,
            Volatile.Read(ref _reampPlaying) == 1,
            Volatile.Read(ref _reampLoop) == 1,
            buf != null && buf.SampleRate > 0 ? Math.Round((double)pos / buf.SampleRate, 2) : 0.0,
            buf != null && buf.SampleRate > 0 ? Math.Round((double)buf.Length / buf.SampleRate, 2) : 0.0,
            buf?.Name ?? "",
            buf?.Path ?? "");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        string cleaned = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? $"NeuralRig_{DateTime.Now:yyyyMMdd_HHmmss}" : cleaned;
    }

    public void Dispose()
    {
        try { StopRecording(); } catch { }
    }
}
