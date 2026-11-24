using System.Runtime.InteropServices;
using NAudio.Wave;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 링 버퍼를 IWaveProvider로 감싼 출력 소스(float32 스테레오).
/// 출력 엔진(WasapiPlayer 또는 AsioDevice.InitPlayback)이 이 소스를 당겨간다.
/// </summary>
public sealed class RingSource : IWaveProvider
{
    private const int MaxResampleInFrames = 8192;

    private readonly RingBuffer _ring;
    private readonly int _inChunk;
    private readonly int _inRate;
    private readonly int _outRate;
    private readonly double _resampleRatio;
    private readonly float[]? _rsBuf;
    private float _prevL, _prevR, _currL, _currR;
    private double _frac;
    private long _target;      // 링 목표 채움(프레임) = 프리롤/드리프트 기준
    private long _ceil;        // 이 값을 초과하면 오래된 프레임 트림
    private int _maxChunk;     // 관측된 최대 출력 청크(프레임)

    public RingSource(RingBuffer ring, int sampleRate, int targetFrames, int inChunkFrames = 0, int outputSampleRate = 0)
    {
        _ring = ring;
        _inChunk = Math.Max(0, inChunkFrames);
        _inRate = Math.Max(8000, sampleRate);
        _outRate = outputSampleRate > 0 ? Math.Max(8000, outputSampleRate) : _inRate;
        _resampleRatio = (double)_inRate / _outRate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_outRate, 2);
        if (_inRate != _outRate)
            _rsBuf = new float[(MaxResampleInFrames + 2) * 2];

        _target = Math.Max(targetFrames, _inChunk > 0 ? _inChunk + 64 : targetFrames);
        _ceil = _target + Math.Max(_inChunk * 2 + 512, _target * 2);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>현재 링 목표 채움량(프레임). 프리롤 대기에 사용.</summary>
    public int TargetFrames => (int)Volatile.Read(ref _target);

    /// <summary>RT 스레드에서 발생하는 통지(UI는 반드시 BeginInvoke로 마샬링).</summary>
    public event Action<string>? Note;

    public int Read(Span<byte> buffer)
    {
        var fspan = MemoryMarshal.Cast<byte, float>(buffer);
        int frames = fspan.Length / 2;
        if (frames <= 0)
        {
            buffer.Clear();
            return buffer.Length;
        }

        int inFramesApprox = _inRate == _outRate ? frames : Math.Max(1, (int)Math.Ceiling(frames * _resampleRatio));

        // 출력 청크가 링 목표보다 크면 언더런 방지를 위해 목표·여유 상한을 함께 상향
        if (inFramesApprox > _maxChunk)
        {
            _maxChunk = inFramesApprox;
            long cur = Volatile.Read(ref _target);
            long want = Math.Max(cur, Math.Max(2L * inFramesApprox + 64, (long)_inChunk + inFramesApprox + 64));
            if (want != cur)
            {
                Volatile.Write(ref _target, want);
                Volatile.Write(ref _ceil, want + Math.Max(_inChunk * 2 + 512, want * 2));
                Note?.Invoke($"출력 청크 {frames}프레임 감지 → 링 목표를 {want}프레임으로 자동 조정");
            }
            else
            {
                Volatile.Write(ref _ceil, cur + Math.Max(_inChunk * 2 + 512, cur * 2));
            }
        }

        long ceil = Volatile.Read(ref _ceil);
        if (_inRate == _outRate || _rsBuf is null)
        {
            _ring.ReadInto(fspan.Slice(0, frames * 2), frames, ceil);
        }
        else
        {
            int maxOutChunk = Math.Max(1, (int)Math.Floor((MaxResampleInFrames - 2) / _resampleRatio));
            int outOffset = 0;
            while (outOffset < frames)
            {
                int chunkOut = Math.Min(frames - outOffset, maxOutChunk);
                double pEnd = _frac + chunkOut * _resampleRatio;
                int consumed = Math.Clamp((int)pEnd, 0, MaxResampleInFrames);

                _rsBuf[0] = _prevL;
                _rsBuf[1] = _prevR;
                _rsBuf[2] = _currL;
                _rsBuf[3] = _currR;

                if (consumed > 0)
                    _ring.ReadInto(_rsBuf.AsSpan(4, consumed * 2), consumed, ceil);

                var outSlice = fspan.Slice(outOffset * 2, chunkOut * 2);
                for (int i = 0; i < chunkOut; i++)
                {
                    double p = _frac + i * _resampleRatio;
                    int k = (int)p;
                    float f = (float)(p - k);
                    int i0 = k * 2;
                    int i1 = (k + 1) * 2;
                    float l0 = _rsBuf[i0];
                    float r0 = _rsBuf[i0 + 1];
                    outSlice[i * 2] = l0 + (_rsBuf[i1] - l0) * f;
                    outSlice[i * 2 + 1] = r0 + (_rsBuf[i1 + 1] - r0) * f;
                }

                int next0 = consumed * 2;
                int next1 = (consumed + 1) * 2;
                _prevL = _rsBuf[next0];
                _prevR = _rsBuf[next0 + 1];
                _currL = _rsBuf[next1];
                _currR = _rsBuf[next1 + 1];
                _frac = pEnd - consumed;
                outOffset += chunkOut;
            }
        }

        fspan.Slice(frames * 2).Clear();
        return buffer.Length;
    }
}
