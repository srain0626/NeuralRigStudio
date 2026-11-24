using System.Buffers.Binary;
using System.Text;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 순수 관리형 무의존성 RIFF/WAVE 코덱 (NativeAOT Trimming / RT 외부 스레드 안전).
/// PCM 8/16/24/32-bit, IEEE Float 32/64-bit, WAVE_FORMAT_EXTENSIBLE 입력 및 IEEE Float32 출력을 지원한다.
/// </summary>
public static class WavFileCodec
{
    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatIeeeFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;

    public static bool TryReadMonoFloat(
        Stream stream,
        int maxSeconds,
        out float[] mono,
        out int sampleRate)
    {
        mono = Array.Empty<float>();
        sampleRate = 48000;

        if (!TryReadInterleavedFloat(stream, maxSeconds, out float[] interleaved, out int channels, out sampleRate))
            return false;

        int frames = interleaved.Length / Math.Max(1, channels);
        if (frames < 8) return false;

        mono = new float[frames];
        if (channels == 1)
        {
            Array.Copy(interleaved, mono, frames);
        }
        else
        {
            float invCh = 1f / channels;
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                int baseIdx = i * channels;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[baseIdx + c];
                mono[i] = sum * invCh;
            }
        }
        return true;
    }

    public static float[] ReadMonoFloat(string wavPath, int maxSeconds, out int sampleRate)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
            throw new FileNotFoundException("WAV 파일을 찾을 수 없습니다.", wavPath);

        using var fs = File.OpenRead(wavPath);
        if (!TryReadMonoFloat(fs, maxSeconds, out float[] mono, out sampleRate) || mono.Length < 8)
            throw new InvalidOperationException("WAV 오디오 파일이 비어 있거나 지원되지 않는 포맷입니다.");

        return mono;
    }

    public static (float[] Left, float[] Right, int SampleRate) ReadStereoFloat(string wavPath, int maxSeconds)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
            throw new FileNotFoundException("WAV 파일을 찾을 수 없습니다.", wavPath);

        using var fs = File.OpenRead(wavPath);
        if (!TryReadInterleavedFloat(fs, maxSeconds, out float[] interleaved, out int ch, out int sr))
            throw new InvalidOperationException("WAV 오디오 파일이 비어 있거나 지원되지 않는 포맷입니다.");

        int frames = interleaved.Length / Math.Max(1, ch);
        if (frames < 8)
            throw new InvalidOperationException("오디오 파일이 비어 있거나 너무 짧습니다.");

        var left = new float[frames];
        var right = new float[frames];
        if (ch == 1)
        {
            Array.Copy(interleaved, left, frames);
            Array.Copy(interleaved, right, frames);
        }
        else
        {
            for (int i = 0; i < frames; i++)
            {
                left[i] = interleaved[i * ch];
                right[i] = interleaved[i * ch + 1];
            }
        }
        return (left, right, sr);
    }

    public static bool TryReadInterleavedFloat(
        Stream stream,
        int maxSeconds,
        out float[] interleaved,
        out int channels,
        out int sampleRate)
    {
        interleaved = Array.Empty<float>();
        channels = 1;
        sampleRate = 48000;

        if (stream == null || !stream.CanRead) return false;
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        byte[] riffHeader = br.ReadBytes(12);
        if (riffHeader.Length < 12) return false;
        if (Encoding.ASCII.GetString(riffHeader, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(riffHeader, 8, 4) != "WAVE")
            return false;

        ushort formatTag = 0;
        ushort bitsPerSample = 0;
        bool hasFmt = false;
        long dataOffset = -1;
        uint dataSize = 0;

        while (true)
        {
            byte[] chunkHdr = br.ReadBytes(8);
            if (chunkHdr.Length < 8) break;

            string chunkId = Encoding.ASCII.GetString(chunkHdr, 0, 4);
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHdr.AsSpan(4, 4));
            long nextChunkPos = stream.Position + chunkSize + (chunkSize & 1u);

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16) return false;
                byte[] fmt = br.ReadBytes((int)Math.Min(chunkSize, 64));
                if (fmt.Length < 16) return false;

                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(0, 2));
                channels = Math.Max(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2, 2)));
                sampleRate = Math.Max(8000, (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4, 4)));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14, 2));

                if (formatTag == WaveFormatExtensible && fmt.Length >= 26)
                {
                    // KSDATAFORMAT_SUBTYPE_PCM:       00000001-0000-0010-8000-00aa00389b71
                    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: 00000003-0000-0010-8000-00aa00389b71
                    uint subTag = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(24, 4));
                    if (subTag == 1) formatTag = WaveFormatPcm;
                    else if (subTag == 3) formatTag = WaveFormatIeeeFloat;
                }
                hasFmt = true;
            }
            else if (chunkId == "data")
            {
                dataOffset = stream.Position;
                dataSize = chunkSize;
                if (hasFmt) break;
            }

            if (stream.CanSeek)
            {
                if (nextChunkPos >= stream.Length) break;
                stream.Position = nextChunkPos;
            }
            else
            {
                long skip = nextChunkPos - stream.Position;
                if (skip > 0) SkipBytes(br, skip);
            }
        }

        if (!hasFmt || dataOffset < 0 || dataSize == 0) return false;
        if (stream.CanSeek) stream.Position = dataOffset;

        int bytesPerSample = Math.Max(1, bitsPerSample / 8);
        int blockAlign = channels * bytesPerSample;
        if (blockAlign <= 0) return false;

        long totalFramesInData = dataSize / (uint)blockAlign;
        long capFrames = maxSeconds > 0 ? Math.Min(totalFramesInData, (long)sampleRate * maxSeconds) : totalFramesInData;
        int framesToRead = (int)Math.Clamp(capFrames, 0, int.MaxValue / Math.Max(1, channels * 4));
        if (framesToRead <= 0) return false;

        int bytesToRead = framesToRead * blockAlign;
        byte[] raw = br.ReadBytes(bytesToRead);
        int actualFrames = raw.Length / blockAlign;
        if (actualFrames <= 0) return false;

        int totalSamples = actualFrames * channels;
        interleaved = new float[totalSamples];

        if (formatTag == WaveFormatIeeeFloat && bitsPerSample == 32)
        {
            for (int i = 0; i < totalSamples; i++)
                interleaved[i] = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(i * 4, 4));
            return true;
        }

        if (formatTag == WaveFormatIeeeFloat && bitsPerSample == 64)
        {
            for (int i = 0; i < totalSamples; i++)
                interleaved[i] = (float)BinaryPrimitives.ReadDoubleLittleEndian(raw.AsSpan(i * 8, 8));
            return true;
        }

        if (formatTag == WaveFormatPcm)
        {
            switch (bitsPerSample)
            {
                case 8:
                    for (int i = 0; i < totalSamples; i++)
                        interleaved[i] = (raw[i] - 128) / 128f;
                    return true;
                case 16:
                    for (int i = 0; i < totalSamples; i++)
                        interleaved[i] = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(i * 2, 2)) / 32768f;
                    return true;
                case 24:
                    for (int i = 0; i < totalSamples; i++)
                    {
                        int off = i * 3;
                        int s = raw[off] | (raw[off + 1] << 8) | ((sbyte)raw[off + 2] << 16);
                        interleaved[i] = s / 8388608f;
                    }
                    return true;
                case 32:
                    for (int i = 0; i < totalSamples; i++)
                        interleaved[i] = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(i * 4, 4)) / 2147483648f;
                    return true;
            }
        }

        return false;
    }

    public static byte[] EncodeMonoFloatWavBytes(float[] samples, int sampleRate)
    {
        int sr = Math.Max(8000, sampleRate);
        int count = samples?.Length ?? 0;
        int dataBytes = count * 4;
        var buf = new byte[44 + dataBytes];
        WriteFloatHeader(buf, sr, 1, (uint)dataBytes);
        for (int i = 0; i < count; i++)
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(44 + i * 4, 4), samples![i]);
        return buf;
    }

    internal static void WriteFloatHeader(Span<byte> hdr, int sampleRate, int channels, uint dataBytes)
    {
        ushort ch = (ushort)Math.Max(1, channels);
        ushort blockAlign = (ushort)(ch * 4);
        uint byteRate = (uint)(Math.Max(8000, sampleRate) * blockAlign);
        uint riffSize = unchecked(36u + dataBytes);

        "RIFF"u8.CopyTo(hdr.Slice(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(4, 4), riffSize);
        "WAVEfmt "u8.CopyTo(hdr.Slice(8, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(16, 4), 16u);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(20, 2), WaveFormatIeeeFloat);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(22, 2), ch);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(24, 4), (uint)Math.Max(8000, sampleRate));
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(32, 2), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(34, 2), 32);
        "data"u8.CopyTo(hdr.Slice(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(40, 4), dataBytes);
    }

    private static void SkipBytes(BinaryReader br, long count)
    {
        var scratch = new byte[4096];
        long rem = count;
        while (rem > 0)
        {
            int n = br.Read(scratch, 0, (int)Math.Min(scratch.Length, rem));
            if (n <= 0) break;
            rem -= n;
        }
    }

    public sealed class FloatWavFileWriter : IDisposable
    {
        private readonly FileStream _fs;
        private readonly int _sampleRate;
        private readonly int _channels;
        private uint _dataBytes;
        private bool _disposed;

        public FloatWavFileWriter(string path, int sampleRate, int channels)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            _sampleRate = Math.Max(8000, sampleRate);
            _channels = Math.Max(1, channels);
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

            Span<byte> hdr = stackalloc byte[44];
            WriteFloatHeader(hdr, _sampleRate, _channels, 0);
            _fs.Write(hdr);
        }

        public void WriteSamples(float[] samples, int offset, int count)
        {
            if (_disposed || count <= 0) return;
            var span = samples.AsSpan(offset, count);
            byte[] buf = new byte[count * 4];
            for (int i = 0; i < count; i++)
                BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(i * 4, 4), span[i]);
            _fs.Write(buf, 0, buf.Length);
            _dataBytes = unchecked(_dataBytes + (uint)buf.Length);
        }

        public void Flush()
        {
            if (_disposed) return;
            PatchSizes();
            _fs.Flush();
        }

        private void PatchSizes()
        {
            long pos = _fs.Position;
            Span<byte> sz = stackalloc byte[4];
            _fs.Position = 4;
            BinaryPrimitives.WriteUInt32LittleEndian(sz, unchecked(36u + _dataBytes));
            _fs.Write(sz);
            _fs.Position = 40;
            BinaryPrimitives.WriteUInt32LittleEndian(sz, _dataBytes);
            _fs.Write(sz);
            _fs.Position = pos;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                PatchSizes();
                _fs.Dispose();
            }
            catch
            {
                try { _fs.Dispose(); } catch { }
            }
        }
    }
}
