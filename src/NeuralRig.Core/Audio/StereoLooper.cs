using System.Numerics;
using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

public enum LooperState
{
    Empty = 0,
    Recording = 1,
    Playing = 2,
    Overdubbing = 3,
    Stopped = 4,
}

public sealed record LooperSnapshotDto(
    string State,
    int LoopLengthFrames,
    int PlayheadFrames,
    double DurationSec,
    double PositionSec,
    double ProgressPct,
    int OverdubLayers,
    bool CanUndo,
    bool CanRedo,
    bool HalfSpeed,
    bool Reverse,
    bool QuantizeSync,
    float Level,
    int SampleRate,
    int MaxDurationSec);

/// <summary>
/// 실시간 무할당 BPM 싱크 스테레오 루퍼 엔진 (Milestone 4 / v1.5).
/// - Record -> Play -> Overdub -> Play -> Stop / Clear 표준 풋스위치 워크플로우
/// - 이중 레이어(_base + _dub) 1-레벨 0-Allocation Undo / Redo + 디클릭 스무딩
/// - 글로벌 TempoBpm 기반 박자 퀀타이즈 스냅(QuantizeSync)
/// - Half-Speed(0.5x) 선형 보간 재생 및 Reverse 역방향 재생
/// - 루프 경계 64샘플 디클릭 크로스페이드
/// </summary>
public sealed class StereoLooper
{
    private const int FadeSamples = 64;
    private const int CmdQueueMask = 15; // 16-slot power-of-2 lock-free SPSC/MPSC queue

    private enum PendingCmd : byte
    {
        None = 0,
        RecDub = 1,
        PlayStop = 2,
        Clear = 3,
    }

    private float[] _baseL = Array.Empty<float>();
    private float[] _baseR = Array.Empty<float>();
    private float[] _dubL = Array.Empty<float>();
    private float[] _dubR = Array.Empty<float>();

    private int _sampleRate = 48000;
    private int _maxSeconds = 60;
    private int _capacityFrames;

    // 명령 링 큐 (UI/MIDI -> RT 스레드)
    private readonly byte[] _cmdRing = new byte[16];
    private int _cmdWrite;
    private int _cmdRead;
    private readonly object _cmdEnqueueLock = new();

    // RT 독점 상태 + UI 읽기용 Volatile 미러
    private int _state = (int)LooperState.Empty;
    private int _loopLength;
    private int _recordedFrames;
    private double _readPos;
    private int _playheadSnapshot;
    private int _lastOverdubIdx = -1;

    private int _hasDubLayer;
    private int _dubActive;
    private int _overdubLayers;

    private int _halfSpeed;
    private int _reverse;
    private int _quantizeSync = 1; // 기본 BPM 싱크 켜짐
    private float _level = 1.0f;

    // 지퍼/클릭 노이즈 방지용 스무딩 게인
    private float _smoothPlayGain;
    private float _smoothDubGain;

    public LooperState State => (LooperState)Volatile.Read(ref _state);
    public bool HasPendingOrActive => Volatile.Read(ref _state) != (int)LooperState.Empty || Volatile.Read(ref _cmdWrite) != Volatile.Read(ref _cmdRead);
    public int LoopLengthFrames => Volatile.Read(ref _loopLength);
    public int PlayheadFrames => Volatile.Read(ref _playheadSnapshot);
    public int OverdubLayers => Volatile.Read(ref _overdubLayers);
    public bool CanUndo => Volatile.Read(ref _hasDubLayer) == 1 && Volatile.Read(ref _dubActive) == 1;
    public bool CanRedo => Volatile.Read(ref _hasDubLayer) == 1 && Volatile.Read(ref _dubActive) == 0;

    public bool HalfSpeed
    {
        get => Volatile.Read(ref _halfSpeed) == 1;
        set => Volatile.Write(ref _halfSpeed, value ? 1 : 0);
    }

    public bool Reverse
    {
        get => Volatile.Read(ref _reverse) == 1;
        set => Volatile.Write(ref _reverse, value ? 1 : 0);
    }

    public bool QuantizeSync
    {
        get => Volatile.Read(ref _quantizeSync) == 1;
        set => Volatile.Write(ref _quantizeSync, value ? 1 : 0);
    }

    public float Level
    {
        get => Volatile.Read(ref _level);
        set => Volatile.Write(ref _level, Math.Clamp(value, 0f, 1.5f));
    }

    /// <summary>스트림 시작 전 1회 사전 할당 (RT 할당 0 B 보장).</summary>
    public void Prepare(int sampleRate, int maxSeconds = 60)
    {
        int sr = Math.Max(8000, sampleRate);
        int secs = Math.Clamp(maxSeconds, 10, 120);
        int cap = sr * secs;
        if (_capacityFrames != cap || _sampleRate != sr)
        {
            _sampleRate = sr;
            _maxSeconds = secs;
            _capacityFrames = cap;
            _baseL = new float[cap];
            _baseR = new float[cap];
            _dubL = new float[cap];
            _dubR = new float[cap];
        }
        ResetAllImmediate();
    }

    public void ResetAllImmediate()
    {
        Volatile.Write(ref _state, (int)LooperState.Empty);
        Volatile.Write(ref _loopLength, 0);
        Volatile.Write(ref _recordedFrames, 0);
        _readPos = 0.0;
        Volatile.Write(ref _playheadSnapshot, 0);
        _lastOverdubIdx = -1;
        Volatile.Write(ref _hasDubLayer, 0);
        Volatile.Write(ref _dubActive, 0);
        Volatile.Write(ref _overdubLayers, 0);
        Volatile.Write(ref _cmdRead, Volatile.Read(ref _cmdWrite));
        _smoothPlayGain = 0f;
        _smoothDubGain = 0f;
    }

    public void TriggerRecordOrOverdub() => EnqueueCommand(PendingCmd.RecDub);

    public void TriggerPlayStop() => EnqueueCommand(PendingCmd.PlayStop);

    public void Clear() => EnqueueCommand(PendingCmd.Clear);

    /// <summary>마지막 오버더빙 레이어를 0 B 할당으로 즉시 되돌린다.</summary>
    public bool Undo()
    {
        if (Volatile.Read(ref _hasDubLayer) == 0 || Volatile.Read(ref _dubActive) == 0)
            return false;
        Volatile.Write(ref _dubActive, 0);
        return true;
    }

    /// <summary>되돌렸던 오버더빙 레이어를 0 B 할당으로 다시 적용한다.</summary>
    public bool Redo()
    {
        if (Volatile.Read(ref _hasDubLayer) == 0 || Volatile.Read(ref _dubActive) == 1)
            return false;
        Volatile.Write(ref _dubActive, 1);
        return true;
    }

    private void EnqueueCommand(PendingCmd cmd)
    {
        lock (_cmdEnqueueLock)
        {
            int w = Volatile.Read(ref _cmdWrite);
            int r = Volatile.Read(ref _cmdRead);
            if (w - r >= 16) return;
            _cmdRing[w & CmdQueueMask] = (byte)cmd;
            Volatile.Write(ref _cmdWrite, w + 1);
        }
    }

    public LooperSnapshotDto GetSnapshot()
    {
        var st = State;
        int len = Volatile.Read(ref _loopLength);
        int sr = Math.Max(8000, _sampleRate);
        int pos = st == LooperState.Recording
            ? Volatile.Read(ref _recordedFrames)
            : (len > 0 ? Math.Clamp(Volatile.Read(ref _playheadSnapshot), 0, len) : 0);

        double durSec = st == LooperState.Recording
            ? (double)pos / sr
            : (len > 0 ? (double)len / sr : 0.0);
        double posSec = (double)pos / sr;
        double progress = st == LooperState.Recording
            ? 100.0
            : (len > 0 ? Math.Clamp(pos * 100.0 / len, 0.0, 100.0) : 0.0);

        string stateStr = st switch
        {
            LooperState.Recording => "recording",
            LooperState.Playing => "playing",
            LooperState.Overdubbing => "overdubbing",
            LooperState.Stopped => "stopped",
            _ => "empty",
        };

        return new LooperSnapshotDto(
            stateStr,
            len,
            pos,
            Math.Round(durSec, 2),
            Math.Round(posSec, 2),
            Math.Round(progress, 1),
            Volatile.Read(ref _overdubLayers),
            CanUndo,
            CanRedo,
            HalfSpeed,
            Reverse,
            QuantizeSync,
            MathF.Round(Level, 2),
            sr,
            _maxSeconds);
    }

    /// <summary>오디오 콜백(RT 스레드)에서 호출. 힙 할당 0 B.</summary>
    public void Process(Span<float> chL, Span<float> chR, int frames, float tempoBpm)
    {
        if (frames <= 0 || _capacityFrames <= 0) return;

        DrainPendingCommands(tempoBpm);

        var st = (LooperState)Volatile.Read(ref _state);
        if (st == LooperState.Empty)
        {
            _smoothPlayGain = 0f;
            _smoothDubGain = 0f;
            return;
        }

        if (st == LooperState.Recording)
        {
            int rec = _recordedFrames;
            int space = _capacityFrames - rec;
            int toWrite = Math.Min(frames, space);
            if (toWrite > 0)
            {
                chL.Slice(0, toWrite).CopyTo(_baseL.AsSpan(rec, toWrite));
                chR.Slice(0, toWrite).CopyTo(_baseR.AsSpan(rec, toWrite));
                rec += toWrite;
                Volatile.Write(ref _recordedFrames, rec);
                Volatile.Write(ref _playheadSnapshot, rec);
            }
            if (rec >= _capacityFrames)
            {
                FinishRecordingToPlay(tempoBpm);
            }
            return;
        }

        int len = _loopLength;
        if (len <= 0) return;

        bool isPlaying = st == LooperState.Playing || st == LooperState.Overdubbing;
        bool isOverdub = st == LooperState.Overdubbing;
        float targetLevel = isPlaying ? Volatile.Read(ref _level) : 0f;
        float targetDub = isOverdub || (Volatile.Read(ref _hasDubLayer) == 1 && Volatile.Read(ref _dubActive) == 1)
            ? 1f
            : 0f;

        // Stopped 상태이고 이미 스무딩 게인이 0으로 수렴했다면 즉시 패스스루
        if (!isPlaying && _smoothPlayGain < 1e-5f)
        {
            _smoothPlayGain = 0f;
            return;
        }

        bool half = Volatile.Read(ref _halfSpeed) == 1;
        bool rev = Volatile.Read(ref _reverse) == 1;
        double step = half ? 0.5 : 1.0;
        if (rev) step = -step;

        const float rampStep = 1f / FadeSamples;
        double pos = _readPos;
        if (pos < 0.0 || pos >= len) pos = WrapPos(pos, len);

        for (int i = 0; i < frames; i++)
        {
            // 지퍼/클릭 노이즈 제거를 위한 64샘플 선형 램프 스무딩
            _smoothPlayGain = MoveToward(_smoothPlayGain, targetLevel, rampStep);
            _smoothDubGain = MoveToward(_smoothDubGain, targetDub, rampStep);

            int idx0 = (int)Math.Floor(pos);
            double frac = pos - idx0;
            if (idx0 >= len) idx0 %= len;
            else if (idx0 < 0) idx0 = (idx0 % len + len) % len;

            int idx1 = idx0 + 1;
            if (idx1 >= len) idx1 = 0;

            float f = (float)frac;
            float baseL = _baseL[idx0] + (_baseL[idx1] - _baseL[idx0]) * f;
            float baseR = _baseR[idx0] + (_baseR[idx1] - _baseR[idx0]) * f;

            float loopL = baseL;
            float loopR = baseR;
            if (_smoothDubGain > 1e-6f)
            {
                float dubL = _dubL[idx0] + (_dubL[idx1] - _dubL[idx0]) * f;
                float dubR = _dubR[idx0] + (_dubR[idx1] - _dubR[idx0]) * f;
                loopL += dubL * _smoothDubGain;
                loopR += dubR * _smoothDubGain;
            }

            float inL = chL[i];
            float inR = chR[i];

            if (isOverdub)
            {
                // Half-Speed일 때 동일 정수 샘플에 중복 합산되는 것을 방지
                if (idx0 != _lastOverdubIdx)
                {
                    _dubL[idx0] += inL;
                    _dubR[idx0] += inR;
                    _lastOverdubIdx = idx0;
                }
            }

            chL[i] = inL + loopL * _smoothPlayGain;
            chR[i] = inR + loopR * _smoothPlayGain;

            pos = WrapPos(pos + step, len);
        }

        _readPos = pos;
        Volatile.Write(ref _playheadSnapshot, Math.Clamp((int)pos, 0, len - 1));
    }

    private void DrainPendingCommands(float tempoBpm)
    {
        int w = Volatile.Read(ref _cmdWrite);
        int r = Volatile.Read(ref _cmdRead);
        if (r == w) return;

        while (r < w)
        {
            var cmd = (PendingCmd)_cmdRing[r & CmdQueueMask];
            r++;
            ApplyCommandOnRt(cmd, tempoBpm);
        }
        Volatile.Write(ref _cmdRead, r);
    }

    private void ApplyCommandOnRt(PendingCmd cmd, float tempoBpm)
    {
        var st = (LooperState)Volatile.Read(ref _state);

        if (cmd == PendingCmd.Clear)
        {
            Volatile.Write(ref _state, (int)LooperState.Empty);
            Volatile.Write(ref _loopLength, 0);
            Volatile.Write(ref _recordedFrames, 0);
            _readPos = 0.0;
            Volatile.Write(ref _playheadSnapshot, 0);
            _lastOverdubIdx = -1;
            Volatile.Write(ref _hasDubLayer, 0);
            Volatile.Write(ref _dubActive, 0);
            Volatile.Write(ref _overdubLayers, 0);
            _smoothPlayGain = 0f;
            _smoothDubGain = 0f;
            return;
        }

        if (cmd == PendingCmd.PlayStop)
        {
            switch (st)
            {
                case LooperState.Recording:
                    FinishRecordingToPlay(tempoBpm);
                    Volatile.Write(ref _state, (int)LooperState.Stopped);
                    _readPos = 0.0;
                    Volatile.Write(ref _playheadSnapshot, 0);
                    break;
                case LooperState.Playing:
                case LooperState.Overdubbing:
                    Volatile.Write(ref _state, (int)LooperState.Stopped);
                    _lastOverdubIdx = -1;
                    break;
                case LooperState.Stopped:
                    if (_loopLength > 0)
                    {
                        _readPos = Volatile.Read(ref _reverse) == 1 ? Math.Max(0, _loopLength - 1) : 0.0;
                        Volatile.Write(ref _playheadSnapshot, (int)_readPos);
                        Volatile.Write(ref _state, (int)LooperState.Playing);
                    }
                    break;
            }
            return;
        }

        if (cmd == PendingCmd.RecDub)
        {
            switch (st)
            {
                case LooperState.Empty:
                    Volatile.Write(ref _loopLength, 0);
                    Volatile.Write(ref _recordedFrames, 0);
                    _readPos = 0.0;
                    Volatile.Write(ref _playheadSnapshot, 0);
                    Volatile.Write(ref _hasDubLayer, 0);
                    Volatile.Write(ref _dubActive, 0);
                    Volatile.Write(ref _overdubLayers, 0);
                    _lastOverdubIdx = -1;
                    Volatile.Write(ref _state, (int)LooperState.Recording);
                    break;

                case LooperState.Recording:
                    FinishRecordingToPlay(tempoBpm);
                    break;

                case LooperState.Playing:
                    PrepareDubLayerForNewOverdub();
                    _lastOverdubIdx = -1;
                    Volatile.Write(ref _state, (int)LooperState.Overdubbing);
                    break;

                case LooperState.Overdubbing:
                    ApplyLoopSeamDeclick(_dubL, _dubR, _loopLength);
                    _lastOverdubIdx = -1;
                    Volatile.Write(ref _state, (int)LooperState.Playing);
                    break;

                case LooperState.Stopped:
                    if (_loopLength > 0)
                    {
                        _readPos = Volatile.Read(ref _reverse) == 1 ? Math.Max(0, _loopLength - 1) : 0.0;
                        Volatile.Write(ref _playheadSnapshot, (int)_readPos);
                        Volatile.Write(ref _state, (int)LooperState.Playing);
                    }
                    break;
            }
        }
    }

    private void FinishRecordingToPlay(float tempoBpm)
    {
        int rec = Volatile.Read(ref _recordedFrames);
        if (rec <= 16)
        {
            Volatile.Write(ref _state, (int)LooperState.Empty);
            Volatile.Write(ref _loopLength, 0);
            Volatile.Write(ref _recordedFrames, 0);
            Volatile.Write(ref _playheadSnapshot, 0);
            return;
        }

        int finalLen = rec;
        if (Volatile.Read(ref _quantizeSync) == 1)
        {
            double bpm = Math.Clamp(tempoBpm > 1f ? tempoBpm : 120f, 40f, 280f);
            double beatFrames = _sampleRate * 60.0 / bpm;
            int beats = Math.Max(1, (int)Math.Round(rec / beatFrames));
            int quantized = (int)Math.Round(beats * beatFrames);
            if (quantized > _capacityFrames)
            {
                int maxBeats = Math.Max(1, (int)Math.Floor(_capacityFrames / beatFrames));
                quantized = Math.Min(_capacityFrames, (int)Math.Round(maxBeats * beatFrames));
            }
            if (quantized > 16)
                finalLen = quantized;
        }

        if (finalLen > rec)
        {
            ApplyTailFadeOut(_baseL, _baseR, rec);
            Array.Clear(_baseL, rec, finalLen - rec);
            Array.Clear(_baseR, rec, finalLen - rec);
        }

        ApplyLoopSeamDeclick(_baseL, _baseR, finalLen);

        Volatile.Write(ref _loopLength, finalLen);
        _readPos = Volatile.Read(ref _reverse) == 1 ? finalLen - 1 : 0.0;
        Volatile.Write(ref _playheadSnapshot, (int)_readPos);
        _smoothPlayGain = 0f;
        Volatile.Write(ref _state, (int)LooperState.Playing);
    }

    private void PrepareDubLayerForNewOverdub()
    {
        int len = _loopLength;
        if (len <= 0) return;

        // 이전 오버더빙 레이어가 활성 상태였다면 _base에 무할당 SIMD 병합 후 _dub 버퍼를 새 레이어용으로 비움
        if (Volatile.Read(ref _hasDubLayer) == 1 && Volatile.Read(ref _dubActive) == 1)
        {
            AddInPlaceSimd(_baseL.AsSpan(0, len), _dubL.AsSpan(0, len));
            AddInPlaceSimd(_baseR.AsSpan(0, len), _dubR.AsSpan(0, len));
        }

        Array.Clear(_dubL, 0, len);
        Array.Clear(_dubR, 0, len);
        Volatile.Write(ref _hasDubLayer, 1);
        Volatile.Write(ref _dubActive, 1);
        Volatile.Write(ref _overdubLayers, Volatile.Read(ref _overdubLayers) + 1);
        _smoothDubGain = 1f;
    }

    private static void ApplyTailFadeOut(float[] left, float[] right, int recordedLen)
    {
        int fade = Math.Min(32, recordedLen / 4);
        if (fade <= 1) return;
        float inv = 1f / fade;
        int start = recordedLen - fade;
        for (int i = 0; i < fade; i++)
        {
            float gOut = 1f - i * inv;
            left[start + i] *= gOut;
            right[start + i] *= gOut;
        }
    }

    private static void ApplyLoopSeamDeclick(float[] left, float[] right, int length)
    {
        int fade = Math.Min(32, length / 8);
        if (fade <= 1) return;
        float inv = 1f / fade;
        int tailStart = length - fade;
        for (int i = 0; i < fade; i++)
        {
            float gIn = i * inv;
            float gOut = 1f - gIn;
            left[i] *= gIn;
            right[i] *= gIn;
            left[tailStart + i] *= gOut;
            right[tailStart + i] *= gOut;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddInPlaceSimd(Span<float> dst, ReadOnlySpan<float> src)
    {
        int len = dst.Length;
        int n = Vector<float>.Count;
        int i = 0;
        if (Vector.IsHardwareAccelerated && len >= n)
        {
            for (; i <= len - n; i += n)
            {
                var vd = new Vector<float>(dst.Slice(i, n));
                var vs = new Vector<float>(src.Slice(i, n));
                (vd + vs).CopyTo(dst.Slice(i, n));
            }
        }
        for (; i < len; i++)
            dst[i] += src[i];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MoveToward(float current, float target, float maxDelta)
    {
        float diff = target - current;
        if (MathF.Abs(diff) <= maxDelta) return target;
        return current + (diff > 0f ? maxDelta : -maxDelta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double WrapPos(double pos, int len)
    {
        double m = pos % len;
        return m < 0.0 ? m + len : m;
    }
}
