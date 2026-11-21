using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NAudio.Wave;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 실시간 ASIO 콜백 스레드용 무할당·락프리 AVX2/FMA 신경망 톤 프로세서.
/// - v3 공식 NAM 호환 Multi-Block Gated Dilated WaveNet (FullRig / AmpHeadOnly+CabIR)
/// - CabIrOnly (512/1024/2048탭 Minimum-Phase 또는 외부 WAV 캐비넷 IR)
/// - v1/v2 레거시 3-레이어 TCN 프로파일 하위 호환
/// </summary>
public sealed class ToneProcessor
{
    public const int HistPad = 64;
    public const int MaxCabTaps = 2048;

    private sealed class LayerRuntimeState
    {
        public readonly float[] Ring;
        public int WritePos;

        public LayerRuntimeState(CompiledWaveNet.Layer layer)
        {
            Ring = new float[layer.RingCapacity * layer.PadChannels];
        }

        public void Reset()
        {
            Array.Clear(Ring);
            WritePos = 0;
        }
    }

    private sealed class ArrayHeadState
    {
        public readonly float[]? HeadRing;
        public int WritePos;

        public ArrayHeadState(CompiledWaveNet.LayerArray arr)
        {
            if (arr.HeadKernelSize > 1)
                HeadRing = new float[arr.HeadRingCapacity * arr.HeadInChannels];
        }

        public void Reset()
        {
            if (HeadRing != null) Array.Clear(HeadRing);
            WritePos = 0;
        }
    }

    /// <summary>
    /// UI 스레드에서 프로파일 선택 시 사전 할당되는 WaveNet 실행 상태(RT 스레드 할당 0).
    /// </summary>
    private sealed class WaveNetWorkspace
    {
        public readonly CompiledWaveNet Net;
        public readonly LayerRuntimeState[][] LayerStates;
        public readonly ArrayHeadState[] HeadStates;

        public readonly float[] YBuf;
        public readonly float[] NextYBuf;
        public readonly float[] MidBuf;
        public readonly float[] PostBuf;
        public readonly float[] HeadAcc;
        public readonly float[] HeadBus;

        public WaveNetWorkspace(CompiledWaveNet net)
        {
            Net = net;
            LayerStates = new LayerRuntimeState[net.Arrays.Length][];
            HeadStates = new ArrayHeadState[net.Arrays.Length];
            for (int bi = 0; bi < net.Arrays.Length; bi++)
            {
                var arr = net.Arrays[bi];
                LayerStates[bi] = new LayerRuntimeState[arr.Layers.Length];
                for (int li = 0; li < arr.Layers.Length; li++)
                    LayerStates[bi][li] = new LayerRuntimeState(arr.Layers[li]);
                HeadStates[bi] = new ArrayHeadState(arr);
            }

            int chCap = Math.Max(16, net.MaxChannelsPadded);
            int midCap = Math.Max(16, net.MaxMidChannelsPadded);
            int headCap = Math.Max(16, net.MaxHeadBusChannelsPadded);
            YBuf = new float[chCap];
            NextYBuf = new float[chCap];
            MidBuf = new float[midCap];
            PostBuf = new float[midCap];
            HeadAcc = new float[headCap];
            HeadBus = new float[headCap];
        }

        public void Reset()
        {
            for (int bi = 0; bi < LayerStates.Length; bi++)
            {
                var ls = LayerStates[bi];
                for (int li = 0; li < ls.Length; li++)
                    ls[li].Reset();
                HeadStates[bi].Reset();
            }
            Array.Clear(YBuf);
            Array.Clear(NextYBuf);
            Array.Clear(MidBuf);
            Array.Clear(PostBuf);
            Array.Clear(HeadAcc);
            Array.Clear(HeadBus);
        }
    }

    private sealed record ProfileRuntime(ToneProfile Profile, WaveNetWorkspace? WaveNet);

    private ProfileRuntime? _runtime;
    private float[]? _customCabIr;
    private float[]? _customCabIrRev;
    private string _customCabIrName = "";
    private string? _customCabIrPath;
    private int _customCabIrRate;

    private int _mode = (int)ToneMode.Bypass;
    private float _driveMul = 1.0f;
    private float _wetMix = 1.0f;
    private float _outGainDb;
    private float _gateThreshDb = -80.0f;

    // 스무딩 상태 (지퍼 노이즈 제거)
    private float _smoothDrive = 1.0f;
    private float _smoothWet = 1.0f;
    private float _smoothOutGain = 1.0f;
    private bool _smoothInitialized;

    private int _sampleRate = 44100;
    private int _maxFrames = 2048;

    // RT 사전 할당 버퍼 (Prepare에서만 할당)
    private float[] _dryL = Array.Empty<float>();
    private float[] _dryR = Array.Empty<float>();
    private float[] _monoWork = Array.Empty<float>();
    private float[] _preHist = Array.Empty<float>();
    private float[] _wsIn = Array.Empty<float>();
    private float[] _wsOut = Array.Empty<float>();
    private float[] _h1 = Array.Empty<float>();
    private float[] _h2 = Array.Empty<float>();
    private float[] _cabHist = Array.Empty<float>();

    // 게이트 / 레거시 컴프레서 / 초저역 DC 블로커 상태
    private float _gateEnv;
    private float _gateGain = 1.0f;
    private bool _gateOpen = true;
    private int _gateHoldSamples;
    private float _compEnv;
    private float _dcX1L, _dcY1L, _dcX1R, _dcY1R;

    public WhiteBoxAmpEngine WhiteBox { get; } = new();
    public SignalChainEngine Chain { get; } = new();

    public ToneMode Mode
    {
        get => (ToneMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    public ToneProfile? ActiveProfile
    {
        get => Volatile.Read(ref _runtime)?.Profile;
        set
        {
            if (value is null)
            {
                Volatile.Write(ref _runtime, null);
                return;
            }
            if (value.CompiledNet is null && value.WaveNetConfig != null && value.Weights.Length > 0)
                value.PrepareForRt();
            var ws = value.CompiledNet != null ? new WaveNetWorkspace(value.CompiledNet) : null;
            Volatile.Write(ref _runtime, new ProfileRuntime(value, ws));
        }
    }

    public string CustomCabIrName => Volatile.Read(ref _customCabIrName);
    public bool HasCustomCabIr => Volatile.Read(ref _customCabIrRev) != null;

    public float[]? GetEffectiveCabIr()
    {
        var custom = Volatile.Read(ref _customCabIr);
        var profCab = Volatile.Read(ref _runtime)?.Profile.CabIr;
        var mode = Mode;
        if (mode == ToneMode.SignalChain)
            return Chain.GetEffectiveCabIr(custom, profCab) ?? custom ?? profCab ?? WhiteBox.DefaultCabIr;
        if (custom != null && custom.Length > 0) return custom;
        if (profCab != null && profCab.Length > 0) return profCab;
        if (mode == ToneMode.WhiteBoxRig || mode == ToneMode.CabIrOnly)
            return WhiteBox.DefaultCabIr;
        return null;
    }

    internal float[]? GetCustomOrProfileCabIrRev()
    {
        var custom = Volatile.Read(ref _customCabIrRev);
        if (custom != null && custom.Length > 0) return custom;
        var prof = Volatile.Read(ref _runtime)?.Profile.CabIrRev;
        return prof is { Length: > 0 } ? prof : null;
    }

    public float DriveMultiplier
    {
        get => Volatile.Read(ref _driveMul);
        set => Volatile.Write(ref _driveMul, Math.Clamp(value, 0.15f, 2.5f));
    }

    public float WetMix
    {
        get => Volatile.Read(ref _wetMix);
        set => Volatile.Write(ref _wetMix, Math.Clamp(value, 0f, 1f));
    }

    public float OutputGainDb
    {
        get => Volatile.Read(ref _outGainDb);
        set => Volatile.Write(ref _outGainDb, Math.Clamp(value, -18f, 18f));
    }

    public float GateThresholdDb
    {
        get => Volatile.Read(ref _gateThreshDb);
        set => Volatile.Write(ref _gateThreshDb, Math.Clamp(value, -80f, -20f));
    }

    /// <summary>외부 .wav 캐비넷 IR을 로드하여 현재 샘플레이트에 맞게 리샘플·위상정렬·정규화 후 원자적으로 교체한다.</summary>
    public (string name, int taps) LoadCustomCabIr(string wavPath)
    {
        if (!File.Exists(wavPath))
            throw new FileNotFoundException("캐비넷 IR 파일을 찾을 수 없습니다.", wavPath);

        using var reader = new AudioFileReader(wavPath);
        int srcRate = reader.WaveFormat.SampleRate;
        int ch = reader.WaveFormat.Channels;
        var raw = new List<float>(Math.Min(srcRate * 2, 192000));
        var chunk = new float[4096 * ch];
        int read;
        while ((read = reader.Read(chunk.AsSpan())) > 0 && raw.Count < srcRate * 2)
        {
            int framesRead = read / ch;
            for (int i = 0; i < framesRead; i++)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++) sum += chunk[i * ch + c];
                raw.Add(sum / ch);
            }
        }
        if (raw.Count < 8)
            throw new InvalidOperationException("IR 파일이 비어 있거나 너무 짧습니다.");

        float[] mono = raw.ToArray();
        int dstRate = _sampleRate > 0 ? _sampleRate : 44100;
        if (srcRate != dstRate && srcRate > 0)
            mono = ResampleLinear(mono, srcRate, dstRate);

        float maxAbs = 0f;
        for (int i = 0; i < mono.Length; i++)
        {
            float a = MathF.Abs(mono[i]);
            if (a > maxAbs) maxAbs = a;
        }
        if (maxAbs < 1e-7f)
            throw new InvalidOperationException("IR 파일에 유효한 임펄스 신호가 없습니다.");

        int start = 0;
        float thresh = maxAbs * 0.006f;
        for (int i = 0; i < Math.Min(mono.Length, dstRate / 4); i++)
        {
            if (MathF.Abs(mono[i]) >= thresh)
            {
                start = Math.Max(0, i - 1);
                break;
            }
        }

        int len = Math.Min(MaxCabTaps, mono.Length - start);
        if (len < 16) len = Math.Min(MaxCabTaps, mono.Length);
        var ir = new float[len];
        Array.Copy(mono, start, ir, 0, len);

        int fade = Math.Min(64, len / 8);
        for (int i = 0; i < fade; i++)
            ir[len - fade + i] *= 1f - (float)i / fade;

        double e = 0.0;
        for (int i = 0; i < len; i++) e += (double)ir[i] * ir[i];
        float norm = (float)Math.Sqrt(e + 1e-12);
        if (norm > 1e-6f)
        {
            for (int i = 0; i < len; i++) ir[i] /= norm;
        }

        var rev = new float[len];
        for (int i = 0; i < len; i++)
            rev[i] = ir[len - 1 - i];

        string name = Path.GetFileName(wavPath);
        _customCabIrPath = wavPath;
        _customCabIrRate = dstRate;
        Volatile.Write(ref _customCabIr, ir);
        Volatile.Write(ref _customCabIrRev, rev);
        Volatile.Write(ref _customCabIrName, name);
        return (name, len);
    }

    public void ClearCustomCabIr()
    {
        _customCabIrPath = null;
        _customCabIrRate = 0;
        Volatile.Write(ref _customCabIr, null);
        Volatile.Write(ref _customCabIrRev, null);
        Volatile.Write(ref _customCabIrName, "");
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

    /// <summary>스트림 시작 전 UI 스레드에서 1회 호출 — RT 작업용 모든 버퍼를 사전 할당한다.</summary>
    public void Prepare(int maxFrames, int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _maxFrames = Math.Max(256, maxFrames);
        if (!string.IsNullOrEmpty(_customCabIrPath) && _customCabIrRate != _sampleRate && File.Exists(_customCabIrPath))
        {
            try { LoadCustomCabIr(_customCabIrPath); } catch { }
        }

        _dryL = new float[_maxFrames];
        _dryR = new float[_maxFrames];
        _monoWork = new float[_maxFrames];
        _preHist = new float[64 + _maxFrames];
        _wsIn = new float[HistPad + _maxFrames];
        _wsOut = new float[_maxFrames];
        _h1 = new float[12 * (HistPad + _maxFrames)];
        _h2 = new float[12 * (HistPad + _maxFrames)];
        _cabHist = new float[MaxCabTaps + _maxFrames];

        WhiteBox.Prepare(_maxFrames, _sampleRate);
        Chain.Prepare(_maxFrames, _sampleRate);
        ResetState();
    }

    public void ResetState()
    {
        if (_preHist.Length > 0) Array.Clear(_preHist);
        if (_wsIn.Length > 0) Array.Clear(_wsIn);
        if (_h1.Length > 0) Array.Clear(_h1);
        if (_h2.Length > 0) Array.Clear(_h2);
        if (_cabHist.Length > 0) Array.Clear(_cabHist);

        WhiteBox.Reset();
        Chain.Reset();
        Volatile.Read(ref _runtime)?.WaveNet?.Reset();

        _gateEnv = 0f;
        _gateGain = 1f;
        _gateOpen = true;
        _gateHoldSamples = 0;
        _compEnv = 0f;
        _dcX1L = _dcY1L = _dcX1R = _dcY1R = 0f;
        _smoothInitialized = false;
    }

    /// <summary>
    /// 순수 WaveNet 추론 전용 진입점(게이트/리미터/DC블로커 없는 수치 동치 검증 및 오프라인 오디션용).
    /// </summary>
    public bool ProcessWaveNetRaw(ReadOnlySpan<float> input, Span<float> output)
    {
        var rt = Volatile.Read(ref _runtime);
        if (rt?.WaveNet is null) return false;
        int n = Math.Min(input.Length, output.Length);
        RunCompiledWaveNet(rt.WaveNet, input.Slice(0, n), output.Slice(0, n));
        return true;
    }

    /// <summary>ASIO 입력 콜백(RT 스레드)에서 호출. 할당/잠금 없음.</summary>
    public void Process(Span<float> chL, Span<float> chR, int frames)
    {
        var mode = (ToneMode)Volatile.Read(ref _mode);
        var rt = Volatile.Read(ref _runtime);
        var prof = rt?.Profile;
        var ws = rt?.WaveNet;
        var customCab = Volatile.Read(ref _customCabIrRev);

        if (mode == ToneMode.Bypass || frames <= 0) return;
        if (frames > _maxFrames) frames = _maxFrames;

        if (mode == ToneMode.SignalChain)
        {
            Chain.Process(chL, chR, frames, this);
            for (int i = 0; i < frames; i++)
            {
                float outL = chL[i];
                float outR = chR[i];
                if (!float.IsFinite(outL)) outL = 0f;
                if (!float.IsFinite(outR)) outR = 0f;

                float yL = outL - _dcX1L + 0.9995f * _dcY1L;
                _dcX1L = outL;
                _dcY1L = yL;
                float yR = outR - _dcX1R + 0.9995f * _dcY1R;
                _dcX1R = outR;
                _dcY1R = yR;

                chL[i] = SoftKneeCeiling(yL);
                chR[i] = SoftKneeCeiling(yR);
            }
            return;
        }

        if (prof is null && customCab is null && mode != ToneMode.WhiteBoxRig && mode != ToneMode.HybridPreampNam)
            return;
        if (prof is null && (mode == ToneMode.FullRig || mode == ToneMode.AmpHeadOnly))
            mode = ToneMode.CabIrOnly;

        // Dry 보관 (Wet/Dry 믹스용)
        chL.Slice(0, frames).CopyTo(_dryL);
        chR.Slice(0, frames).CopyTo(_dryR);

        // SIMD 모노 합산 입력: 0.5 * (L + R)
        AverageStereoSimd(chL.Slice(0, frames), chR.Slice(0, frames), _monoWork.AsSpan(0, frames));

        // 1) 노이즈 게이트 (히스테리시스 + 160ms 홀드 + 부드러운 릴리즈, -78dB 이하 바이패스)
        float gateDb = Volatile.Read(ref _gateThreshDb);
        if (gateDb > -78f)
        {
            float threshOpen = MathF.Pow(10f, gateDb / 20f);
            float threshClose = threshOpen * 0.5623413f;
            int holdMax = Math.Max(64, (int)(0.160f * _sampleRate));
            float envAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0005f * _sampleRate));
            float envRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.060f * _sampleRate));
            float gainAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0015f * _sampleRate));
            float gainRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.220f * _sampleRate));

            for (int i = 0; i < frames; i++)
            {
                float a = MathF.Abs(_monoWork[i]);
                _gateEnv += (a > _gateEnv ? envAtk : envRel) * (a - _gateEnv);

                if (_gateEnv >= threshOpen)
                {
                    _gateOpen = true;
                    _gateHoldSamples = holdMax;
                }
                else if (_gateOpen)
                {
                    if (_gateEnv >= threshClose)
                        _gateHoldSamples = holdMax;
                    else if (_gateHoldSamples > 0)
                        _gateHoldSamples--;
                    else
                        _gateOpen = false;
                }

                float target = _gateOpen ? 1.0f : 0.01f;
                float coeff = target > _gateGain ? gainAtk : gainRel;
                _gateGain += coeff * (target - _gateGain);
                _monoWork[i] *= _gateGain;
            }
        }
        else
        {
            _gateOpen = true;
            _gateHoldSamples = 0;
            _gateGain = 1.0f;
        }

        // 파라미터 스무딩 목표값
        float targetDrive = Volatile.Read(ref _driveMul);
        float targetWet = Volatile.Read(ref _wetMix);
        float targetOutGain = MathF.Pow(10f, Volatile.Read(ref _outGainDb) / 20f);
        if (!_smoothInitialized)
        {
            _smoothDrive = targetDrive;
            _smoothWet = targetWet;
            _smoothOutGain = targetOutGain;
            _smoothInitialized = true;
        }
        float paramAlpha = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.004f * _sampleRate));

        bool isWaveNet = ws != null;
        bool runAmp = (mode == ToneMode.FullRig || mode == ToneMode.AmpHeadOnly) && prof != null;

        // 2) 조합형 하이브리드 라우팅 (WhiteBoxRig / HybridPreampNam / NAM WaveNet / CabIrOnly)
        if (mode == ToneMode.WhiteBoxRig)
        {
            ApplySmoothedDrive(frames, targetDrive, paramAlpha);
            WhiteBox.ProcessFullAmp(_monoWork.AsSpan(0, frames), frames, includePowerAmp: WhiteBox.IsPowerAmpEnabled);
            var cabRev = customCab ?? (prof?.CabIrRev is { Length: > 0 } ? prof.CabIrRev : WhiteBox.DefaultCabIrRev);
            ApplyCabIr(cabRev, frames);
        }
        else if (mode == ToneMode.HybridPreampNam)
        {
            ApplySmoothedDrive(frames, targetDrive, paramAlpha);
            WhiteBox.ProcessFullAmp(_monoWork.AsSpan(0, frames), frames, includePowerAmp: WhiteBox.IsPowerAmpEnabled);
            if (isWaveNet)
            {
                RunCompiledWaveNet(ws!, _monoWork.AsSpan(0, frames), _monoWork.AsSpan(0, frames));
                if (customCab != null)
                    ApplyCabIr(customCab, frames);
            }
            else if (prof != null && prof.HasLegacyNeuralNet)
            {
                RunLegacyNeuralBlock(prof, frames, 1.0f);
                var cabRev = customCab ?? (prof.CabIrRev is { Length: > 0 } ? prof.CabIrRev : WhiteBox.DefaultCabIrRev);
                ApplyCabIr(cabRev, frames);
            }
            else
            {
                var cabRev = customCab ?? (prof?.CabIrRev is { Length: > 0 } ? prof.CabIrRev : WhiteBox.DefaultCabIrRev);
                ApplyCabIr(cabRev, frames);
            }
        }
        else if (runAmp && isWaveNet)
        {
            if (WhiteBox.IsTsEnabled)
                WhiteBox.ProcessTs808Only(_monoWork.AsSpan(0, frames), frames);

            ApplySmoothedDrive(frames, targetDrive, paramAlpha);
            RunCompiledWaveNet(ws!, _monoWork.AsSpan(0, frames), _monoWork.AsSpan(0, frames));

            // AmpHeadOnly(3)일 때만 WaveNet 뒤에 외부/내장 Cab IR 추가 컨볼루션
            if (mode == ToneMode.AmpHeadOnly)
            {
                var cabRev = customCab ?? (prof!.CabIrRev is { Length: > 0 } ? prof.CabIrRev : WhiteBox.DefaultCabIrRev);
                ApplyCabIr(cabRev, frames);
            }
        }
        else if (runAmp && prof!.HasLegacyNeuralNet)
        {
            if (WhiteBox.IsTsEnabled)
                WhiteBox.ProcessTs808Only(_monoWork.AsSpan(0, frames), frames);

            _smoothDrive += MathF.Min(1f, paramAlpha * frames) * (targetDrive - _smoothDrive);
            RunLegacyNeuralBlock(prof, frames, _smoothDrive);

            var cabRev = customCab ?? prof.CabIrRev;
            ApplyCabIr(cabRev, frames);
        }
        else
        {
            // CabIrOnly(2) 또는 WaveNet/레거시 가중치가 없는 레퍼런스 Cab-IR 프로파일
            var cabRev = customCab ?? (prof?.CabIrRev is { Length: > 0 } ? prof.CabIrRev : WhiteBox.DefaultCabIrRev);
            ApplyCabIr(cabRev, frames);
        }

        // 3) 투명 출력 게인 + Wet/Dry 믹스 + 초저역 DC 블로커(fc ~ 3.5Hz) + 0.92 투명 소프트-니 리미터
        for (int i = 0; i < frames; i++)
        {
            _smoothOutGain += paramAlpha * (targetOutGain - _smoothOutGain);
            _smoothWet += paramAlpha * (targetWet - _smoothWet);

            float m = _monoWork[i] * _smoothOutGain;
            float wet = _smoothWet;
            float dry = 1f - wet;

            float outL = wet * m + dry * _dryL[i];
            float outR = wet * m + dry * _dryR[i];

            if (!float.IsFinite(outL)) outL = 0f;
            if (!float.IsFinite(outR)) outR = 0f;

            // 초저역 3.5Hz DC 블로커 (기타 70Hz~10kHz 위상 왜곡 0에 근접)
            float yL = outL - _dcX1L + 0.9995f * _dcY1L;
            _dcX1L = outL;
            _dcY1L = yL;
            float yR = outR - _dcX1R + 0.9995f * _dcY1R;
            _dcX1R = outR;
            _dcY1R = yR;

            chL[i] = SoftKneeCeiling(yL);
            chR[i] = SoftKneeCeiling(yR);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplySmoothedDrive(int frames, float targetDrive, float paramAlpha)
    {
        if (MathF.Abs(_smoothDrive - 1f) > 1e-4f || MathF.Abs(targetDrive - 1f) > 1e-4f)
        {
            for (int i = 0; i < frames; i++)
            {
                _smoothDrive += paramAlpha * (targetDrive - _smoothDrive);
                _monoWork[i] *= _smoothDrive;
            }
        }
        else
        {
            _smoothDrive = 1f;
        }
    }

    private void RunLegacyNeuralBlock(ToneProfile prof, int frames, float driveMul)
    {
        var preRev = prof.PreEqRev;
        int preLen = preRev.Length;
        if (preLen > 1 && preLen <= 64)
        {
            int histN = preLen - 1;
            _monoWork.AsSpan(0, frames).CopyTo(_preHist.AsSpan(histN, frames));
            for (int i = 0; i < frames; i++)
                _wsIn[HistPad + i] = DotSimd(_preHist.AsSpan(i, preLen), preRev);
            _preHist.AsSpan(frames, histN).CopyTo(_preHist.AsSpan(0, histN));
        }
        else
        {
            _monoWork.AsSpan(0, frames).CopyTo(_wsIn.AsSpan(HistPad, frames));
        }

        float gain = prof.PreGain * driveMul;
        float bias = prof.Bias;
        float tanhBias = MathF.Tanh(bias);
        TubeShapeSimd(_wsIn.AsSpan(HistPad, frames), gain, bias, tanhBias);

        if (MathF.Abs(prof.NetScale) > 1e-4f)
        {
            RunLegacyNeuralWaveshaper(prof, frames);
            ResidualMixSimd(_wsIn.AsSpan(HistPad, frames), _wsOut.AsSpan(0, frames), prof.NetScale, _monoWork.AsSpan(0, frames));
        }
        else
        {
            _wsIn.AsSpan(HistPad, frames).CopyTo(_monoWork.AsSpan(0, frames));
        }
        _wsIn.AsSpan(frames, HistPad).CopyTo(_wsIn.AsSpan(0, HistPad));

        float compThresh = MathF.Pow(10f, prof.CompThresholdDb / 20f);
        float compRatio = Math.Max(1f, prof.CompRatio);
        float slope = 1f - 1f / compRatio;
        float legacyOut = prof.OutputGain;
        for (int i = 0; i < frames; i++)
        {
            float s = _monoWork[i] * legacyOut;
            float a = MathF.Abs(s);
            _compEnv = a > _compEnv ? 0.95f * _compEnv + 0.05f * a : 0.9992f * _compEnv + 0.0008f * a;
            if (_compEnv > compThresh && _compEnv > 1e-6f)
                s *= MathF.Pow(_compEnv / compThresh, -slope);
            _monoWork[i] = s;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyCabIr(float[] cabRev, int frames)
    {
        int cabLen = cabRev.Length;
        if (cabLen <= 0 || cabLen > MaxCabTaps) return;
        int histN = cabLen - 1;
        _monoWork.AsSpan(0, frames).CopyTo(_cabHist.AsSpan(histN, frames));
        for (int i = 0; i < frames; i++)
            _monoWork[i] = DotSimd(_cabHist.AsSpan(i, cabLen), cabRev);
        _cabHist.AsSpan(frames, histN).CopyTo(_cabHist.AsSpan(0, histN));
    }

    /// <summary>시그널 체인 Cab IR 블록용 무할당 SIMD 컨볼루션 진입점.</summary>
    internal void ApplyCabIrExternal(Span<float> mono, int frames, bool preferCustom)
    {
        var customCab = Volatile.Read(ref _customCabIrRev);
        var profCab = Volatile.Read(ref _runtime)?.Profile.CabIrRev;
        float[]? cabRev = preferCustom
            ? (customCab ?? (profCab is { Length: > 0 } ? profCab : WhiteBox.DefaultCabIrRev))
            : (WhiteBox.DefaultCabIrRev is { Length: > 0 } ? WhiteBox.DefaultCabIrRev : customCab);
        if (cabRev is null || cabRev.Length <= 0 || cabRev.Length > MaxCabTaps) return;

        int cabLen = cabRev.Length;
        int histN = cabLen - 1;
        mono.Slice(0, frames).CopyTo(_cabHist.AsSpan(histN, frames));
        for (int i = 0; i < frames; i++)
            mono[i] = DotSimd(_cabHist.AsSpan(i, cabLen), cabRev);
        _cabHist.AsSpan(frames, histN).CopyTo(_cabHist.AsSpan(0, histN));
    }

    // =========================================================================
    // AVX2 / FMA 공식 NAM Multi-Block Gated Dilated WaveNet 실시간 추론 커널
    // =========================================================================
    private static void RunCompiledWaveNet(WaveNetWorkspace ws, ReadOnlySpan<float> input, Span<float> output)
    {
        var net = ws.Net;
        var arrays = net.Arrays;
        int nArrays = arrays.Length;
        int frames = input.Length;
        float headScale = net.HeadScale;

        ref float yRef = ref MemoryMarshal.GetArrayDataReference(ws.YBuf);
        ref float nextYRef = ref MemoryMarshal.GetArrayDataReference(ws.NextYBuf);
        ref float midRef = ref MemoryMarshal.GetArrayDataReference(ws.MidBuf);
        ref float postRef = ref MemoryMarshal.GetArrayDataReference(ws.PostBuf);
        ref float headAccRef = ref MemoryMarshal.GetArrayDataReference(ws.HeadAcc);
        ref float headBusRef = ref MemoryMarshal.GetArrayDataReference(ws.HeadBus);

        for (int t = 0; t < frames; t++)
        {
            float x0 = input[t];
            yRef = x0;

            for (int bi = 0; bi < nArrays; bi++)
            {
                var arr = arrays[bi];
                int inCh = arr.InputSize;
                int ch = arr.Channels;
                int padCh = arr.PadChannels;

                // 1) Rechannel: y[inCh] -> nextY[padCh] (no bias), then copy to y
                ref float rechW = ref MemoryMarshal.GetArrayDataReference(arr.RechannelW);
                ScaleRow(ref nextYRef, ref rechW, yRef, padCh);
                for (int ic = 1; ic < inCh; ic++)
                {
                    float v = Unsafe.Add(ref yRef, ic);
                    MacRow(ref nextYRef, ref Unsafe.Add(ref rechW, ic * padCh), v, padCh);
                }
                CopyVec(ref yRef, ref nextYRef, padCh);

                // 2) Head accumulator init
                int headInPad = Pad8(arr.HeadInChannels);
                if (bi == 0)
                    ZeroVec(ref headAccRef, headInPad);
                else
                    CopyVec(ref headAccRef, ref headBusRef, headInPad);

                // 3) Dilated causal layers
                var layers = arr.Layers;
                var lStates = ws.LayerStates[bi];
                for (int li = 0; li < layers.Length; li++)
                {
                    var layer = layers[li];
                    var st = lStates[li];
                    int wPos = st.WritePos;
                    int ringMask = layer.RingMask;
                    ref float ringBase = ref MemoryMarshal.GetArrayDataReference(st.Ring);

                    // 현재 잔차 y를 링 버퍼 wPos에 기록
                    CopyVec(ref Unsafe.Add(ref ringBase, wPos * padCh), ref yRef, padCh);

                    // 3a) Dilated Causal Conv1d + Bias + InputMixer (레지스터 고정 AVX2/FMA)
                    int kSize = layer.KernelSize;
                    int dil = layer.Dilation;
                    int padMid = layer.PadMidChannels;
                    ref float convW = ref MemoryMarshal.GetArrayDataReference(layer.ConvW);
                    ref float convB = ref MemoryMarshal.GetArrayDataReference(layer.ConvB);
                    ref float mixW = ref MemoryMarshal.GetArrayDataReference(layer.InputMixerW);

                    ConvDilatedFma(
                        ref midRef,
                        ref convB,
                        ref mixW,
                        x0,
                        ref ringBase,
                        ref convW,
                        wPos,
                        ringMask,
                        kSize,
                        dil,
                        ch,
                        padCh,
                        padMid);

                    // 3b) Activation (+ Gating)
                    int bn = layer.Bottleneck;
                    if (layer.Gated)
                        ActivateGated(ref midRef, ref postRef, bn, layer.Act);
                    else
                        ActivatePlain(ref midRef, ref postRef, bn, layer.Act);

                    // 3c) Residual 1x1: y = y + Layer1x1(post) (or y + post)
                    if (layer.Layer1x1Active)
                    {
                        ref float l1w = ref MemoryMarshal.GetArrayDataReference(layer.Layer1x1W);
                        ref float l1b = ref MemoryMarshal.GetArrayDataReference(layer.Layer1x1B);
                        ResidualLayer1x1Fma(ref yRef, ref l1b, ref l1w, ref postRef, bn, padCh);
                    }
                    else
                    {
                        AddScalarRange(ref yRef, ref postRef, bn);
                    }

                    // 3d) Skip Head1x1 accumulation into headAcc
                    if (layer.Head1x1Active)
                    {
                        ref float h1w = ref MemoryMarshal.GetArrayDataReference(layer.Head1x1W);
                        int padH1Out = layer.PadHeadOut;
                        for (int ic = 0; ic < bn; ic++)
                        {
                            float pv = Unsafe.Add(ref postRef, ic);
                            MacRow(ref headAccRef, ref Unsafe.Add(ref h1w, ic * padH1Out), pv, padH1Out);
                        }
                    }
                    else
                    {
                        AddScalarRange(ref headAccRef, ref postRef, bn);
                    }

                    st.WritePos = (wPos + 1) & ringMask;
                }

                // 4) Head Rechannel: headAcc -> headBus
                int hInCh = arr.HeadInChannels;
                int padHOut = arr.PadHeadOutChannels;
                ref float hRechW = ref MemoryMarshal.GetArrayDataReference(arr.HeadRechannelW);
                ref float hRechB = ref MemoryMarshal.GetArrayDataReference(arr.HeadRechannelB);
                CopyVec(ref headBusRef, ref hRechB, padHOut);

                if (arr.HeadKernelSize == 1)
                {
                    for (int ic = 0; ic < hInCh; ic++)
                    {
                        float hv = Unsafe.Add(ref headAccRef, ic);
                        MacRow(ref headBusRef, ref Unsafe.Add(ref hRechW, ic * padHOut), hv, padHOut);
                    }
                }
                else
                {
                    var hst = ws.HeadStates[bi];
                    int hwPos = hst.WritePos;
                    int hMask = arr.HeadRingMask;
                    ref float hRing = ref MemoryMarshal.GetArrayDataReference(hst.HeadRing!);
                    ref float dstSlot = ref Unsafe.Add(ref hRing, hwPos * hInCh);
                    for (int ic = 0; ic < hInCh; ic++)
                        Unsafe.Add(ref dstSlot, ic) = Unsafe.Add(ref headAccRef, ic);

                    int hK = arr.HeadKernelSize;
                    for (int k = 0; k < hK; k++)
                    {
                        int slot = (hwPos - (hK - 1 - k)) & hMask;
                        ref float srcSlot = ref Unsafe.Add(ref hRing, slot * hInCh);
                        int wKBase = k * hInCh * padHOut;
                        for (int ic = 0; ic < hInCh; ic++)
                        {
                            float hv = Unsafe.Add(ref srcSlot, ic);
                            MacRow(ref headBusRef, ref Unsafe.Add(ref hRechW, wKBase + ic * padHOut), hv, padHOut);
                        }
                    }
                    hst.WritePos = (hwPos + 1) & hMask;
                }
            }

            output[t] = headBusRef * headScale;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Pad8(int n) => (n + 7) & ~7;

    /// <summary>
    /// 레지스터 누적 AVX2/FMA 인과 Dilated Conv1d + InputMixer 커널.
    /// Nano(24), Feather(24/16), Standard(32/16)의 출력 채널을 YMM 레지스터에 고정하여 L1 쓰기를 최소화한다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvDilatedFma(
        ref float midRef,
        ref float convB,
        ref float mixW,
        float x0,
        ref float ringBase,
        ref float convW,
        int wPos,
        int ringMask,
        int kSize,
        int dil,
        int ch,
        int padCh,
        int padMid)
    {
        if (Fma.IsSupported)
        {
            var vx0 = Vector256.Create(x0);
            if (padMid == 24)
            {
                var a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 0), vx0, Vector256.LoadUnsafe(ref convB, 0));
                var a1 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 8), vx0, Vector256.LoadUnsafe(ref convB, 8));
                var a2 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 16), vx0, Vector256.LoadUnsafe(ref convB, 16));

                int wOffset = 0;
                for (int k = 0; k < kSize; k++)
                {
                    int slot = (wPos - (kSize - 1 - k) * dil) & ringMask;
                    ref float rRow = ref Unsafe.Add(ref ringBase, slot * padCh);
                    for (int ic = 0; ic < ch; ic++)
                    {
                        var v = Vector256.Create(Unsafe.Add(ref rRow, ic));
                        ref float wRow = ref Unsafe.Add(ref convW, wOffset);
                        a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 0), v, a0);
                        a1 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 8), v, a1);
                        a2 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 16), v, a2);
                        wOffset += 24;
                    }
                }
                Vector256.StoreUnsafe(a0, ref midRef, 0);
                Vector256.StoreUnsafe(a1, ref midRef, 8);
                Vector256.StoreUnsafe(a2, ref midRef, 16);
                return;
            }
            if (padMid == 32)
            {
                var a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 0), vx0, Vector256.LoadUnsafe(ref convB, 0));
                var a1 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 8), vx0, Vector256.LoadUnsafe(ref convB, 8));
                var a2 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 16), vx0, Vector256.LoadUnsafe(ref convB, 16));
                var a3 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 24), vx0, Vector256.LoadUnsafe(ref convB, 24));

                int wOffset = 0;
                for (int k = 0; k < kSize; k++)
                {
                    int slot = (wPos - (kSize - 1 - k) * dil) & ringMask;
                    ref float rRow = ref Unsafe.Add(ref ringBase, slot * padCh);
                    for (int ic = 0; ic < ch; ic++)
                    {
                        var v = Vector256.Create(Unsafe.Add(ref rRow, ic));
                        ref float wRow = ref Unsafe.Add(ref convW, wOffset);
                        a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 0), v, a0);
                        a1 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 8), v, a1);
                        a2 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 16), v, a2);
                        a3 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 24), v, a3);
                        wOffset += 32;
                    }
                }
                Vector256.StoreUnsafe(a0, ref midRef, 0);
                Vector256.StoreUnsafe(a1, ref midRef, 8);
                Vector256.StoreUnsafe(a2, ref midRef, 16);
                Vector256.StoreUnsafe(a3, ref midRef, 24);
                return;
            }
            if (padMid == 16)
            {
                var a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 0), vx0, Vector256.LoadUnsafe(ref convB, 0));
                var a1 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 8), vx0, Vector256.LoadUnsafe(ref convB, 8));

                int wOffset = 0;
                for (int k = 0; k < kSize; k++)
                {
                    int slot = (wPos - (kSize - 1 - k) * dil) & ringMask;
                    ref float rRow = ref Unsafe.Add(ref ringBase, slot * padCh);
                    for (int ic = 0; ic < ch; ic++)
                    {
                        var v = Vector256.Create(Unsafe.Add(ref rRow, ic));
                        ref float wRow = ref Unsafe.Add(ref convW, wOffset);
                        a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 0), v, a0);
                        a1 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 8), v, a1);
                        wOffset += 16;
                    }
                }
                Vector256.StoreUnsafe(a0, ref midRef, 0);
                Vector256.StoreUnsafe(a1, ref midRef, 8);
                return;
            }
            if (padMid == 8)
            {
                var a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref mixW, 0), vx0, Vector256.LoadUnsafe(ref convB, 0));
                int wOffset = 0;
                for (int k = 0; k < kSize; k++)
                {
                    int slot = (wPos - (kSize - 1 - k) * dil) & ringMask;
                    ref float rRow = ref Unsafe.Add(ref ringBase, slot * padCh);
                    for (int ic = 0; ic < ch; ic++)
                    {
                        var v = Vector256.Create(Unsafe.Add(ref rRow, ic));
                        a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref Unsafe.Add(ref convW, wOffset), 0), v, a0);
                        wOffset += 8;
                    }
                }
                Vector256.StoreUnsafe(a0, ref midRef, 0);
                return;
            }
        }

        // 범용 경로 (임의 대형 .nam 채널 폭 지원)
        CopyVec(ref midRef, ref convB, padMid);
        MacRow(ref midRef, ref mixW, x0, padMid);
        int genOffset = 0;
        for (int k = 0; k < kSize; k++)
        {
            int slot = (wPos - (kSize - 1 - k) * dil) & ringMask;
            ref float rRow = ref Unsafe.Add(ref ringBase, slot * padCh);
            for (int ic = 0; ic < ch; ic++)
            {
                float v = Unsafe.Add(ref rRow, ic);
                MacRow(ref midRef, ref Unsafe.Add(ref convW, genOffset), v, padMid);
                genOffset += padMid;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResidualLayer1x1Fma(
        ref float yRef,
        ref float l1b,
        ref float l1w,
        ref float postRef,
        int bn,
        int padCh)
    {
        if (Fma.IsSupported)
        {
            if (padCh == 16)
            {
                var a0 = Vector256.LoadUnsafe(ref yRef, 0) + Vector256.LoadUnsafe(ref l1b, 0);
                var a1 = Vector256.LoadUnsafe(ref yRef, 8) + Vector256.LoadUnsafe(ref l1b, 8);
                int wOff = 0;
                for (int ic = 0; ic < bn; ic++)
                {
                    var v = Vector256.Create(Unsafe.Add(ref postRef, ic));
                    ref float wRow = ref Unsafe.Add(ref l1w, wOff);
                    a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 0), v, a0);
                    a1 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref wRow, 8), v, a1);
                    wOff += 16;
                }
                Vector256.StoreUnsafe(a0, ref yRef, 0);
                Vector256.StoreUnsafe(a1, ref yRef, 8);
                return;
            }
            if (padCh == 8)
            {
                var a0 = Vector256.LoadUnsafe(ref yRef, 0) + Vector256.LoadUnsafe(ref l1b, 0);
                int wOff = 0;
                for (int ic = 0; ic < bn; ic++)
                {
                    var v = Vector256.Create(Unsafe.Add(ref postRef, ic));
                    a0 = Fma.MultiplyAdd(Vector256.LoadUnsafe(ref Unsafe.Add(ref l1w, wOff), 0), v, a0);
                    wOff += 8;
                }
                Vector256.StoreUnsafe(a0, ref yRef, 0);
                return;
            }
        }

        AddVec(ref yRef, ref l1b, padCh);
        for (int ic = 0; ic < bn; ic++)
        {
            float pv = Unsafe.Add(ref postRef, ic);
            MacRow(ref yRef, ref Unsafe.Add(ref l1w, ic * padCh), pv, padCh);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ActivateGated(ref float midRef, ref float postRef, int bn, WaveNetActKind act)
    {
        int oc = 0;
        if (Vector256.IsHardwareAccelerated && bn >= 8)
        {
            for (; oc <= bn - 8; oc += 8)
            {
                var va = Vector256.LoadUnsafe(ref midRef, (nuint)oc);
                var vb = Vector256.LoadUnsafe(ref midRef, (nuint)(bn + oc));
                var res = ApplyAct256(va, act) * FastSigmoid256(vb);
                Vector256.StoreUnsafe(res, ref postRef, (nuint)oc);
            }
        }
        for (; oc < bn; oc++)
        {
            float a = Unsafe.Add(ref midRef, oc);
            float b = Unsafe.Add(ref midRef, bn + oc);
            Unsafe.Add(ref postRef, oc) = ApplyActScalar(a, act) * FastSigmoidScalar(b);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ActivatePlain(ref float midRef, ref float postRef, int bn, WaveNetActKind act)
    {
        int oc = 0;
        if (Vector256.IsHardwareAccelerated && bn >= 8)
        {
            for (; oc <= bn - 8; oc += 8)
            {
                var va = Vector256.LoadUnsafe(ref midRef, (nuint)oc);
                Vector256.StoreUnsafe(ApplyAct256(va, act), ref postRef, (nuint)oc);
            }
        }
        for (; oc < bn; oc++)
        {
            Unsafe.Add(ref postRef, oc) = ApplyActScalar(Unsafe.Add(ref midRef, oc), act);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> ApplyAct256(Vector256<float> x, WaveNetActKind act) => act switch
    {
        WaveNetActKind.Tanh => FastTanh256(x),
        WaveNetActKind.SiLU => x * FastSigmoid256(x),
        WaveNetActKind.ReLU => Vector256.Max(Vector256<float>.Zero, x),
        WaveNetActKind.LeakyReLU => Vector256.ConditionalSelect(
            Vector256.GreaterThanOrEqual(x, Vector256<float>.Zero),
            x,
            x * Vector256.Create(0.01f)),
        WaveNetActKind.Sigmoid => FastSigmoid256(x),
        _ => FastTanh256(x),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ApplyActScalar(float x, WaveNetActKind act) => act switch
    {
        WaveNetActKind.Tanh => FastTanhScalar(x),
        WaveNetActKind.SiLU => x * FastSigmoidScalar(x),
        WaveNetActKind.ReLU => x > 0f ? x : 0f,
        WaveNetActKind.LeakyReLU => x >= 0f ? x : 0.01f * x,
        WaveNetActKind.Sigmoid => FastSigmoidScalar(x),
        _ => FastTanhScalar(x),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> FastTanh256(Vector256<float> x)
    {
        x = Vector256.Min(Vector256.Create(4.97f), Vector256.Max(Vector256.Create(-4.97f), x));
        var x2 = x * x;
        var num = x * (Vector256.Create(135135f) + x2 * (Vector256.Create(17325f) + x2 * (Vector256.Create(378f) + x2)));
        var den = Vector256.Create(135135f) + x2 * (Vector256.Create(62370f) + x2 * (Vector256.Create(3150f) + Vector256.Create(28f) * x2));
        return num / den;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> FastSigmoid256(Vector256<float> x)
    {
        var half = Vector256.Create(0.5f);
        return half + half * FastTanh256(x * half);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastSigmoidScalar(float x) => 0.5f + 0.5f * FastTanhScalar(0.5f * x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftKneeCeiling(float y)
    {
        float ay = MathF.Abs(y);
        if (ay <= 0.92f) return y;
        float limited = 0.92f + 0.075f * FastTanhScalar((ay - 0.92f) / 0.075f);
        return y >= 0f ? limited : -limited;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZeroVec(ref float dst, int padLen)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            var z = Vector256<float>.Zero;
            for (int i = 0; i < padLen; i += 8)
                Vector256.StoreUnsafe(z, ref dst, (nuint)i);
            return;
        }
        for (int i = 0; i < padLen; i++) Unsafe.Add(ref dst, i) = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyVec(ref float dst, ref float src, int padLen)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            for (int i = 0; i < padLen; i += 8)
                Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref src, (nuint)i), ref dst, (nuint)i);
            return;
        }
        for (int i = 0; i < padLen; i++) Unsafe.Add(ref dst, i) = Unsafe.Add(ref src, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddVec(ref float dst, ref float src, int padLen)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            for (int i = 0; i < padLen; i += 8)
            {
                var v = Vector256.LoadUnsafe(ref dst, (nuint)i) + Vector256.LoadUnsafe(ref src, (nuint)i);
                Vector256.StoreUnsafe(v, ref dst, (nuint)i);
            }
            return;
        }
        for (int i = 0; i < padLen; i++) Unsafe.Add(ref dst, i) += Unsafe.Add(ref src, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddScalarRange(ref float dst, ref float src, int len)
    {
        int i = 0;
        if (Vector256.IsHardwareAccelerated && len >= 8)
        {
            for (; i <= len - 8; i += 8)
            {
                var v = Vector256.LoadUnsafe(ref dst, (nuint)i) + Vector256.LoadUnsafe(ref src, (nuint)i);
                Vector256.StoreUnsafe(v, ref dst, (nuint)i);
            }
        }
        for (; i < len; i++) Unsafe.Add(ref dst, i) += Unsafe.Add(ref src, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScaleRow(ref float dst, ref float wRow, float scalar, int padLen)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            var vs = Vector256.Create(scalar);
            for (int i = 0; i < padLen; i += 8)
                Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref wRow, (nuint)i) * vs, ref dst, (nuint)i);
            return;
        }
        for (int i = 0; i < padLen; i++) Unsafe.Add(ref dst, i) = Unsafe.Add(ref wRow, i) * scalar;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MacRow(ref float dst, ref float wRow, float scalar, int padLen)
    {
        if (Fma.IsSupported)
        {
            var vs = Vector256.Create(scalar);
            for (int i = 0; i < padLen; i += 8)
            {
                var vd = Vector256.LoadUnsafe(ref dst, (nuint)i);
                var vw = Vector256.LoadUnsafe(ref wRow, (nuint)i);
                Vector256.StoreUnsafe(Fma.MultiplyAdd(vw, vs, vd), ref dst, (nuint)i);
            }
            return;
        }
        if (Vector256.IsHardwareAccelerated)
        {
            var vs = Vector256.Create(scalar);
            for (int i = 0; i < padLen; i += 8)
            {
                var vd = Vector256.LoadUnsafe(ref dst, (nuint)i);
                var vw = Vector256.LoadUnsafe(ref wRow, (nuint)i);
                Vector256.StoreUnsafe(vd + vw * vs, ref dst, (nuint)i);
            }
            return;
        }
        for (int i = 0; i < padLen; i++) Unsafe.Add(ref dst, i) += Unsafe.Add(ref wRow, i) * scalar;
    }

    // =========================================================================
    // 레거시 v1/v2 3계층 TCN 및 공용 SIMD 유틸리티
    // =========================================================================
    private void RunLegacyNeuralWaveshaper(ToneProfile prof, int frames)
    {
        int stride = HistPad + _maxFrames;
        var w1 = prof.Conv1W;
        var b1 = prof.Conv1B;
        var w2 = prof.Conv2W;
        var b2 = prof.Conv2B;
        var w3 = prof.Conv3W;
        var b3 = prof.Conv3B;

        int d1 = prof.Dilation1;
        int d2 = prof.Dilation2;
        int d3 = prof.Dilation3;

        ReadOnlySpan<float> in0 = _wsIn.AsSpan(HistPad - 2 * d1, frames);
        ReadOnlySpan<float> in1 = _wsIn.AsSpan(HistPad - d1, frames);
        ReadOnlySpan<float> in2 = _wsIn.AsSpan(HistPad, frames);

        for (int oc = 0; oc < 12; oc++)
        {
            var dst = _h1.AsSpan(oc * stride + HistPad, frames);
            dst.Fill(b1[oc]);
            AddMul3Simd(in0, w1[oc * 3], in1, w1[oc * 3 + 1], in2, w1[oc * 3 + 2], dst);
            TanhInPlaceSimd(dst);
        }

        for (int oc = 0; oc < 12; oc++)
        {
            int dstRow = oc * stride;
            var dst = _h2.AsSpan(dstRow + HistPad, frames);
            dst.Fill(b2[oc]);
            int wBaseOc = oc * 36;
            for (int ic = 0; ic < 12; ic++)
            {
                int srcRow = ic * stride;
                int wb = wBaseOc + ic * 3;
                AddMul3Simd(
                    _h1.AsSpan(srcRow + HistPad - 2 * d2, frames), w2[wb],
                    _h1.AsSpan(srcRow + HistPad - d2, frames), w2[wb + 1],
                    _h1.AsSpan(srcRow + HistPad, frames), w2[wb + 2],
                    dst);
            }
            TanhInPlaceSimd(dst);
        }

        var outSpan = _wsOut.AsSpan(0, frames);
        outSpan.Fill(b3[0]);
        for (int ic = 0; ic < 12; ic++)
        {
            int srcRow = ic * stride;
            int wb = ic * 3;
            AddMul3Simd(
                _h2.AsSpan(srcRow + HistPad - 2 * d3, frames), w3[wb],
                _h2.AsSpan(srcRow + HistPad - d3, frames), w3[wb + 1],
                _h2.AsSpan(srcRow + HistPad, frames), w3[wb + 2],
                outSpan);
        }

        for (int c = 0; c < 12; c++)
        {
            int r = c * stride;
            _h1.AsSpan(r + frames, HistPad).CopyTo(_h1.AsSpan(r, HistPad));
            _h2.AsSpan(r + frames, HistPad).CopyTo(_h2.AsSpan(r, HistPad));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AverageStereoSimd(ReadOnlySpan<float> l, ReadOnlySpan<float> r, Span<float> dst)
    {
        int len = dst.Length;
        int n = Vector<float>.Count;
        var half = new Vector<float>(0.5f);
        int i = 0;
        if (Vector.IsHardwareAccelerated && len >= n)
        {
            for (; i <= len - n; i += n)
            {
                var vl = new Vector<float>(l.Slice(i, n));
                var vr = new Vector<float>(r.Slice(i, n));
                ((vl + vr) * half).CopyTo(dst.Slice(i, n));
            }
        }
        for (; i < len; i++)
            dst[i] = 0.5f * (l[i] + r[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TubeShapeSimd(Span<float> buf, float gain, float bias, float tanhBias)
    {
        int len = buf.Length;
        int n = Vector<float>.Count;
        var vg = new Vector<float>(gain);
        var vb = new Vector<float>(bias);
        var vtb = new Vector<float>(tanhBias);
        int i = 0;
        if (Vector.IsHardwareAccelerated && len >= n)
        {
            for (; i <= len - n; i += n)
            {
                var vx = new Vector<float>(buf.Slice(i, n)) * vg + vb;
                (FastTanhVec(vx) - vtb).CopyTo(buf.Slice(i, n));
            }
        }
        for (; i < len; i++)
            buf[i] = FastTanhScalar(buf[i] * gain + bias) - tanhBias;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResidualMixSimd(ReadOnlySpan<float> baseIn, ReadOnlySpan<float> res, float scale, Span<float> dst)
    {
        int len = dst.Length;
        int n = Vector<float>.Count;
        var vs = new Vector<float>(scale);
        int i = 0;
        if (Vector.IsHardwareAccelerated && len >= n)
        {
            for (; i <= len - n; i += n)
            {
                var vb = new Vector<float>(baseIn.Slice(i, n));
                var vr = new Vector<float>(res.Slice(i, n));
                (vb + vr * vs).CopyTo(dst.Slice(i, n));
            }
        }
        for (; i < len; i++)
            dst[i] = baseIn[i] + scale * res[i];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddMul3Simd(
        ReadOnlySpan<float> s0, float w0,
        ReadOnlySpan<float> s1, float w1,
        ReadOnlySpan<float> s2, float w2,
        Span<float> dst)
    {
        int len = dst.Length;
        int n = Vector<float>.Count;
        var vw0 = new Vector<float>(w0);
        var vw1 = new Vector<float>(w1);
        var vw2 = new Vector<float>(w2);
        int i = 0;
        if (Vector.IsHardwareAccelerated && len >= n)
        {
            for (; i <= len - n; i += n)
            {
                var acc = new Vector<float>(dst.Slice(i, n));
                acc += new Vector<float>(s0.Slice(i, n)) * vw0;
                acc += new Vector<float>(s1.Slice(i, n)) * vw1;
                acc += new Vector<float>(s2.Slice(i, n)) * vw2;
                acc.CopyTo(dst.Slice(i, n));
            }
        }
        for (; i < len; i++)
            dst[i] += s0[i] * w0 + s1[i] * w1 + s2[i] * w2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TanhInPlaceSimd(Span<float> buf)
    {
        int len = buf.Length;
        int n = Vector<float>.Count;
        int i = 0;
        if (Vector.IsHardwareAccelerated && len >= n)
        {
            for (; i <= len - n; i += n)
            {
                var v = new Vector<float>(buf.Slice(i, n));
                FastTanhVec(v).CopyTo(buf.Slice(i, n));
            }
        }
        for (; i < len; i++)
            buf[i] = FastTanhScalar(buf[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> FastTanhVec(Vector<float> x)
    {
        var lo = new Vector<float>(-4.97f);
        var hi = new Vector<float>(4.97f);
        x = Vector.Min(hi, Vector.Max(lo, x));
        var x2 = x * x;
        var num = x * (new Vector<float>(135135f) + x2 * (new Vector<float>(17325f) + x2 * (new Vector<float>(378f) + x2)));
        var den = new Vector<float>(135135f) + x2 * (new Vector<float>(62370f) + x2 * (new Vector<float>(3150f) + new Vector<float>(28f) * x2));
        return num / den;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastTanhScalar(float x)
    {
        if (x < -4.97f) return -1f;
        if (x > 4.97f) return 1f;
        float x2 = x * x;
        float num = x * (135135f + x2 * (17325f + x2 * (378f + x2)));
        float den = 135135f + x2 * (62370f + x2 * (3150f + 28f * x2));
        return num / den;
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

