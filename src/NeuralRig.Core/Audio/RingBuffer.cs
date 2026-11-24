using System.Numerics;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 단일 생산자(ASIO 입력 콜백) / 단일 소비자(출력 콜백)용 무잠금 스테레오 float 링 버퍼.
/// 2의 거듭제곱 용량 + 비트 마스크(&amp; _mask) + 연속 블록 루프로 RT 나눗셈 오버헤드를 제거한다.
/// </summary>
public sealed class RingBuffer
{
    private readonly float[] _buf;   // 인터리브된 스테레오 [L,R,L,R,...]
    private readonly int _cap;       // 2의 거듭제곱 프레임 단위 용량
    private readonly int _mask;
    private long _w;                 // 생산자만 수정
    private long _r;                 // 소비자만 수정
    private long _lostNewest;        // 버퍼가 가득 차 새 데이터 유실(프레임)
    private long _droppedOldest;     // 지연 한도 초과로 오래된 데이터 폐기(프레임)
    private long _padded;            // 부족분 무음 패딩(프레임)

    public RingBuffer(int capacityFrames)
    {
        uint pow2 = BitOperations.RoundUpToPowerOf2((uint)Math.Max(1024, capacityFrames));
        _cap = (int)pow2;
        _mask = _cap - 1;
        _buf = new float[_cap * 2];
    }

    public int CapacityFrames => _cap;

    /// <summary>현재 링에 쌓여 있는 프레임 수(지연의 실측치).</summary>
    public long FillFrames
    {
        get
        {
            long w = Volatile.Read(ref _w);
            long r = Volatile.Read(ref _r);
            return Math.Max(0, w - r);
        }
    }

    public long LostNewest => Volatile.Read(ref _lostNewest);
    public long DroppedOldest => Volatile.Read(ref _droppedOldest);
    public long PaddedFrames => Volatile.Read(ref _padded);

    /// <summary>스트림이 중지된 유휴 상태에서 링 포인터와 통계를 초기화한다.</summary>
    public void Reset()
    {
        Volatile.Write(ref _w, 0);
        Volatile.Write(ref _r, 0);
        Volatile.Write(ref _lostNewest, 0);
        Volatile.Write(ref _droppedOldest, 0);
        Volatile.Write(ref _padded, 0);
    }

    /// <summary>백그라운드 소비자(레코더 등)에서 현재 쌓여 있는 프레임만 무음 패딩 없이 읽어온다.</summary>
    public int ReadAvailable(Span<float> dstInterleaved, int maxFrames)
    {
        if (maxFrames <= 0 || dstInterleaved.Length < 2) return 0;
        int capOut = Math.Min(maxFrames, dstInterleaved.Length / 2);
        long w = Volatile.Read(ref _w);
        long r = Volatile.Read(ref _r);
        long fill = Math.Max(0, w - r);
        int avail = (int)Math.Min(fill, capOut);
        if (avail <= 0) return 0;

        int idx = (int)(r & _mask);
        int first = Math.Min(avail, _cap - idx);
        int baseSrc = idx * 2;
        for (int i = 0; i < first; i++)
        {
            dstInterleaved[i * 2] = _buf[baseSrc + i * 2];
            dstInterleaved[i * 2 + 1] = _buf[baseSrc + i * 2 + 1];
        }

        int second = avail - first;
        int outOffset = first * 2;
        for (int i = 0; i < second; i++)
        {
            dstInterleaved[outOffset + i * 2] = _buf[i * 2];
            dstInterleaved[outOffset + i * 2 + 1] = _buf[i * 2 + 1];
        }

        Volatile.Write(ref _r, r + avail);
        return avail;
    }

    /// <summary>입력 콜백(RT 스레드)에서 호출. 할당/나눗셈 없음.</summary>
    public void Write(int frames, ReadOnlySpan<float> ch0, ReadOnlySpan<float> ch1)
    {
        if (frames <= 0) return;
        long w = Volatile.Read(ref _w);
        long r = Volatile.Read(ref _r);
        long free = _cap - (w - r);
        if (frames > free)
        {
            int skip = (int)Math.Min(frames - free, frames);
            Interlocked.Add(ref _lostNewest, skip);
            frames -= skip;
            if (frames <= 0) return;
            ch0 = ch0.Slice(ch0.Length - frames);
            ch1 = ch1.Slice(ch1.Length - frames);
        }

        int idx = (int)(w & _mask);
        int first = Math.Min(frames, _cap - idx);
        int baseDst = idx * 2;
        for (int i = 0; i < first; i++)
        {
            _buf[baseDst + i * 2] = ch0[i];
            _buf[baseDst + i * 2 + 1] = ch1[i];
        }

        int second = frames - first;
        for (int i = 0; i < second; i++)
        {
            _buf[i * 2] = ch0[first + i];
            _buf[i * 2 + 1] = ch1[first + i];
        }

        Volatile.Write(ref _w, w + frames);
    }

    /// <summary>출력 콜백(RT 스레드)에서 호출. dst를 최대 frames 프레임만큼 채우고, 부족분은 무음으로 채운다.</summary>
    /// <returns>실제로 링에서 읽어 채운 프레임 수</returns>
    public int ReadInto(Span<float> dst, int frames, long trimCeil)
    {
        long w = Volatile.Read(ref _w);
        long r = Volatile.Read(ref _r);
        long fill = Math.Max(0, w - r);
        long excess = fill - trimCeil;
        if (excess > 0)
        {
            r += excess;
            fill -= excess;
            Volatile.Write(ref _r, r);
            Interlocked.Add(ref _droppedOldest, excess);
        }

        int avail = (int)Math.Min(fill, frames);
        if (avail > 0)
        {
            int idx = (int)(r & _mask);
            int first = Math.Min(avail, _cap - idx);
            int baseSrc = idx * 2;
            for (int i = 0; i < first; i++)
            {
                dst[i * 2] = _buf[baseSrc + i * 2];
                dst[i * 2 + 1] = _buf[baseSrc + i * 2 + 1];
            }

            int second = avail - first;
            int outOffset = first * 2;
            for (int i = 0; i < second; i++)
            {
                dst[outOffset + i * 2] = _buf[i * 2];
                dst[outOffset + i * 2 + 1] = _buf[i * 2 + 1];
            }

            Volatile.Write(ref _r, r + avail);
        }

        if (avail < frames)
        {
            dst.Slice(avail * 2, (frames - avail) * 2).Clear();
            Interlocked.Add(ref _padded, frames - avail);
        }

        return avail;
    }
}

