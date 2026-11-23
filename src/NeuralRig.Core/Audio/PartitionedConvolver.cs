using System.Numerics;
using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 컴파일된 Zero-Latency Uniformly Partitioned Overlap-Save FFT 임펄스 응답.
/// - DirectRev: 0..255탭 직접 SIMD 컨볼루션(지연 0 샘플 보장)
/// - Tail Partitions: 256샘플 블록 단위 512-pt Radix-2 FFT 주파수 도메인 파티션 (최대 16,384탭)
/// </summary>
public sealed class PartitionedIr
{
    public const int BlockSize = 256;
    public const int FftSize = 512;
    public const int Bins = FftSize / 2 + 1; // 257
    public const int MaxTaps = 16384;
    public const int MaxTailPartitions = (MaxTaps - BlockSize) / BlockSize; // 63
    public const int FdlSlots = 64; // power of 2 >= MaxTailPartitions

    public float[] RawIr { get; }
    public float[] DirectRev { get; }
    public int TailPartitions { get; }
    public float[][] PartRe { get; }
    public float[][] PartIm { get; }

    private PartitionedIr(float[] rawIr, float[] directRev, int tailPartitions, float[][] partRe, float[][] partIm)
    {
        RawIr = rawIr;
        DirectRev = directRev;
        TailPartitions = tailPartitions;
        PartRe = partRe;
        PartIm = partIm;
    }

    public static PartitionedIr Compile(ReadOnlySpan<float> ir)
    {
        int len = Math.Clamp(ir.Length, 1, MaxTaps);
        var raw = new float[len];
        ir.Slice(0, len).CopyTo(raw);

        int directLen = Math.Min(len, BlockSize);
        var directRev = new float[directLen];
        for (int i = 0; i < directLen; i++)
            directRev[i] = raw[directLen - 1 - i];

        int tailSamples = Math.Max(0, len - BlockSize);
        int tailParts = (tailSamples + BlockSize - 1) / BlockSize;
        var partRe = new float[tailParts][];
        var partIm = new float[tailParts][];

        if (tailParts > 0)
        {
            var fft = new Radix2Fft512();
            var tmpRe = new float[FftSize];
            var tmpIm = new float[FftSize];
            for (int p = 0; p < tailParts; p++)
            {
                Array.Clear(tmpRe);
                Array.Clear(tmpIm);
                int srcOff = (p + 1) * BlockSize;
                int copyLen = Math.Min(BlockSize, len - srcOff);
                for (int i = 0; i < copyLen; i++)
                    tmpRe[i] = raw[srcOff + i];

                fft.Forward(tmpRe, tmpIm);

                var r = new float[Bins];
                var im = new float[Bins];
                Array.Copy(tmpRe, 0, r, 0, Bins);
                Array.Copy(tmpIm, 0, im, 0, Bins);
                partRe[p] = r;
                partIm[p] = im;
            }
        }

        return new PartitionedIr(raw, directRev, tailParts, partRe, partIm);
    }
}

internal sealed class Radix2Fft512
{
    private const int N = PartitionedIr.FftSize; // 512
    private readonly int[] _bitRev = new int[N];
    private readonly float[] _cosFwd = new float[N / 2];
    private readonly float[] _sinFwd = new float[N / 2];

    public Radix2Fft512()
    {
        for (int i = 0; i < N; i++)
        {
            int x = i;
            int r = 0;
            for (int b = 0; b < 9; b++)
            {
                r = (r << 1) | (x & 1);
                x >>= 1;
            }
            _bitRev[i] = r;
        }
        for (int k = 0; k < N / 2; k++)
        {
            double ang = -2.0 * Math.PI * k / N;
            _cosFwd[k] = (float)Math.Cos(ang);
            _sinFwd[k] = (float)Math.Sin(ang);
        }
    }

    public void Forward(float[] re, float[] im) => Transform(re, im, inverse: false);
    public void Inverse(float[] re, float[] im) => Transform(re, im, inverse: true);

    private void Transform(float[] re, float[] im, bool inverse)
    {
        for (int i = 0; i < N; i++)
        {
            int j = _bitRev[i];
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        float invSign = inverse ? -1f : 1f;
        for (int len = 2; len <= N; len <<= 1)
        {
            int half = len >> 1;
            int step = N / len;
            for (int i = 0; i < N; i += len)
            {
                int twIdx = 0;
                for (int j = 0; j < half; j++)
                {
                    float wRe = _cosFwd[twIdx];
                    float wIm = _sinFwd[twIdx] * invSign;
                    twIdx += step;

                    int u = i + j;
                    int v = u + half;
                    float rv = re[v];
                    float iv = im[v];
                    float tRe = rv * wRe - iv * wIm;
                    float tIm = rv * wIm + iv * wRe;
                    re[v] = re[u] - tRe;
                    im[v] = im[u] - tIm;
                    re[u] += tRe;
                    im[u] += tIm;
                }
            }
        }

        if (inverse)
        {
            const float invN = 1f / N;
            for (int i = 0; i < N; i++)
            {
                re[i] *= invN;
                im[i] *= invN;
            }
        }
    }
}

/// <summary>
/// 실시간 Zero-Allocation 스테레오 Hybrid Partitioned FFT 컨볼버 (최대 16,384탭, 지연 0 샘플).
/// - 모노 입력 자동 감지 시 R 채널 FFT/FDL 연산을 생략하여 CPU 50% 절감.
/// </summary>
public sealed class StereoPartitionedConvolver
{
    private const int B = PartitionedIr.BlockSize;
    private const int N = PartitionedIr.FftSize;
    private const int Bins = PartitionedIr.Bins;
    private const int FdlSlots = PartitionedIr.FdlSlots;
    private const int FdlMask = FdlSlots - 1;

    private readonly Radix2Fft512 _fft = new();

    private float[] _directHistL = Array.Empty<float>();
    private float[] _directHistR = Array.Empty<float>();

    private readonly float[] _fftInL = new float[N];
    private readonly float[] _fftInR = new float[N];
    private readonly float[] _tailOutL = new float[B];
    private readonly float[] _tailOutR = new float[B];

    private readonly float[][] _fdlReL = CreateFdl();
    private readonly float[][] _fdlImL = CreateFdl();
    private readonly float[][] _fdlReR = CreateFdl();
    private readonly float[][] _fdlImR = CreateFdl();

    private readonly float[] _fftRe = new float[N];
    private readonly float[] _fftIm = new float[N];
    private readonly float[] _accRe = new float[Bins];
    private readonly float[] _accIm = new float[Bins];

    private int _inputFill;
    private int _fdlHead;
    private PartitionedIr? _lastIr;

    private static float[][] CreateFdl()
    {
        var arr = new float[FdlSlots][];
        for (int i = 0; i < FdlSlots; i++)
            arr[i] = new float[Bins];
        return arr;
    }

    public void Prepare(int maxFrames)
    {
        int len = B + Math.Max(256, maxFrames);
        _directHistL = new float[len];
        _directHistR = new float[len];
        Reset();
    }

    public void Reset()
    {
        if (_directHistL.Length > 0) Array.Clear(_directHistL);
        if (_directHistR.Length > 0) Array.Clear(_directHistR);
        Array.Clear(_fftInL);
        Array.Clear(_fftInR);
        Array.Clear(_tailOutL);
        Array.Clear(_tailOutR);
        for (int i = 0; i < FdlSlots; i++)
        {
            Array.Clear(_fdlReL[i]);
            Array.Clear(_fdlImL[i]);
            Array.Clear(_fdlReR[i]);
            Array.Clear(_fdlImR[i]);
        }
        _inputFill = 0;
        _fdlHead = 0;
    }

    public void ConvolveStereo(Span<float> bufL, Span<float> bufR, int frames, PartitionedIr ir)
    {
        if (frames <= 0 || _directHistL.Length < B + frames) return;
        if (!ReferenceEquals(_lastIr, ir))
        {
            Reset();
            _lastIr = ir;
        }

        var directRev = ir.DirectRev;
        int directLen = directRev.Length;
        int histN = directLen - 1;
        int tailParts = ir.TailPartitions;

        bool isMono = true;
        for (int i = 0; i < frames; i++)
        {
            if (MathF.Abs(bufL[i] - bufR[i]) > 1e-6f)
            {
                isMono = false;
                break;
            }
        }

        int pos = 0;
        while (pos < frames)
        {
            int step = tailParts > 0
                ? Math.Min(frames - pos, B - _inputFill)
                : (frames - pos);

            // 1) Copy step samples for FFT overlap-save current block before overwriting bufL/R
            if (tailParts > 0)
            {
                bufL.Slice(pos, step).CopyTo(_fftInL.AsSpan(B + _inputFill, step));
                if (isMono)
                    bufL.Slice(pos, step).CopyTo(_fftInR.AsSpan(B + _inputFill, step));
                else
                    bufR.Slice(pos, step).CopyTo(_fftInR.AsSpan(B + _inputFill, step));
            }

            // 2) Direct 0..255 tap convolution + pre-computed tail overlap-add
            bufL.Slice(pos, step).CopyTo(_directHistL.AsSpan(histN, step));
            if (isMono)
            {
                for (int i = 0; i < step; i++)
                {
                    float y = DotSimd(_directHistL.AsSpan(i, directLen), directRev);
                    if (tailParts > 0) y += _tailOutL[_inputFill + i];
                    bufL[pos + i] = y;
                    bufR[pos + i] = y;
                }
                _directHistL.AsSpan(step, histN).CopyTo(_directHistL.AsSpan(0, histN));
                _directHistL.AsSpan(0, histN).CopyTo(_directHistR.AsSpan(0, histN));
            }
            else
            {
                bufR.Slice(pos, step).CopyTo(_directHistR.AsSpan(histN, step));
                for (int i = 0; i < step; i++)
                {
                    float yL = DotSimd(_directHistL.AsSpan(i, directLen), directRev);
                    float yR = DotSimd(_directHistR.AsSpan(i, directLen), directRev);
                    if (tailParts > 0)
                    {
                        yL += _tailOutL[_inputFill + i];
                        yR += _tailOutR[_inputFill + i];
                    }
                    bufL[pos + i] = yL;
                    bufR[pos + i] = yR;
                }
                _directHistL.AsSpan(step, histN).CopyTo(_directHistL.AsSpan(0, histN));
                _directHistR.AsSpan(step, histN).CopyTo(_directHistR.AsSpan(0, histN));
            }

            pos += step;
            _inputFill += step;

            // 3) When 256-sample block completes, compute next block's FFT tail
            if (tailParts > 0 && _inputFill >= B)
            {
                ComputeTailBlock(_fftInL, _fdlReL, _fdlImL, _tailOutL, ir);
                if (isMono)
                {
                    Array.Copy(_fdlReL[_fdlHead], _fdlReR[_fdlHead], Bins);
                    Array.Copy(_fdlImL[_fdlHead], _fdlImR[_fdlHead], Bins);
                    Array.Copy(_tailOutL, _tailOutR, B);
                }
                else
                {
                    ComputeTailBlock(_fftInR, _fdlReR, _fdlImR, _tailOutR, ir);
                }

                // Shift overlap-save input window [Prev | Cur] -> [Cur | ...]
                Array.Copy(_fftInL, B, _fftInL, 0, B);
                Array.Copy(_fftInR, B, _fftInR, 0, B);

                _fdlHead = (_fdlHead + 1) & FdlMask;
                _inputFill = 0;
            }
        }
    }

    private void ComputeTailBlock(float[] fftIn, float[][] fdlRe, float[][] fdlIm, float[] tailOut, PartitionedIr ir)
    {
        // Forward 512-pt FFT of [Prev256 | Cur256]
        Array.Copy(fftIn, 0, _fftRe, 0, N);
        Array.Clear(_fftIm);
        _fft.Forward(_fftRe, _fftIm);

        Array.Copy(_fftRe, 0, fdlRe[_fdlHead], 0, Bins);
        Array.Copy(_fftIm, 0, fdlIm[_fdlHead], 0, Bins);

        Array.Clear(_accRe);
        Array.Clear(_accIm);

        int tailParts = ir.TailPartitions;
        var partRe = ir.PartRe;
        var partIm = ir.PartIm;

        for (int p = 0; p < tailParts; p++)
        {
            int slot = (_fdlHead - p) & FdlMask;
            ComplexMacSimd(fdlRe[slot], fdlIm[slot], partRe[p], partIm[p], _accRe, _accIm);
        }

        // Reconstruct Hermitian conjugate symmetry for 512-pt real IFFT
        Array.Copy(_accRe, 0, _fftRe, 0, Bins);
        Array.Copy(_accIm, 0, _fftIm, 0, Bins);
        for (int k = 1; k < N / 2; k++)
        {
            _fftRe[N - k] = _accRe[k];
            _fftIm[N - k] = -_accIm[k];
        }

        _fft.Inverse(_fftRe, _fftIm);
        Array.Copy(_fftRe, B, tailOut, 0, B);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ComplexMacSimd(
        float[] aRe, float[] aIm,
        float[] bRe, float[] bIm,
        float[] accRe, float[] accIm)
    {
        int n = Vector<float>.Count;
        int i = 0;
        if (Vector.IsHardwareAccelerated && Bins >= n)
        {
            int limit = Bins - n;
            for (; i <= limit; i += n)
            {
                var ar = new Vector<float>(aRe, i);
                var ai = new Vector<float>(aIm, i);
                var br = new Vector<float>(bRe, i);
                var bi = new Vector<float>(bIm, i);
                var cr = new Vector<float>(accRe, i) + (ar * br - ai * bi);
                var ci = new Vector<float>(accIm, i) + (ar * bi + ai * br);
                cr.CopyTo(accRe, i);
                ci.CopyTo(accIm, i);
            }
        }
        for (; i < Bins; i++)
        {
            float ar = aRe[i], ai = aIm[i];
            float br = bRe[i], bi = bIm[i];
            accRe[i] += ar * br - ai * bi;
            accIm[i] += ar * bi + ai * br;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DotSimd(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int len = a.Length;
        int n = Vector<float>.Count;
        int i = 0;
        var vacc = Vector<float>.Zero;
        if (Vector.IsHardwareAccelerated && len >= n)
        {
            for (; i <= len - n; i += n)
            {
                var va = new Vector<float>(a.Slice(i, n));
                var vb = new Vector<float>(b.Slice(i, n));
                vacc += va * vb;
            }
        }
        float sum = Vector.Dot(vacc, Vector<float>.One);
        for (; i < len; i++)
            sum += a[i] * b[i];
        return sum;
    }
}
