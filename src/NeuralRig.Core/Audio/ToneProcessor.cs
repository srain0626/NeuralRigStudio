using System.Numerics;
using System.Runtime.CompilerServices;

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
    public const int MaxCabTaps = PartitionedIr.MaxTaps;

    private sealed record ProfileRuntime(ToneProfile Profile, NamWorkspace? Nam, PartitionedIr? CabPartIr);

    private ProfileRuntime? _runtime;
    private Dictionary<string, ToneProfile> _modelRegistry = new(StringComparer.OrdinalIgnoreCase);
    private float[]? _customCabIr;
    private float[]? _customCabIrRev;
    private PartitionedIr? _customCabPartIr;
    private string _customCabIrName = "";
    private string? _customCabIrPath;
    private int _customCabIrRate;
    private readonly StereoPartitionedConvolver _singleCabConvolver = new();
    private float[] _monoCabR = Array.Empty<float>();

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
    private float _rtInPeakL;
    private float _rtInPeakR;
    private int _rtInClipFlag;
    private int _rtOutClipFlag;

    public WhiteBoxAmpEngine WhiteBox { get; } = new();
    public SignalChainEngine Chain { get; } = new();
    public StereoLooper Looper { get; } = new();

    public ToneProcessor()
    {
        Chain.SetNamModelResolver(ResolveModel);
    }

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
                Chain.RefreshNamBindings();
                return;
            }
            if (!value.HasNeuralNet && value.Weights.Length > 0 &&
                (value.WaveNetConfig != null || value.LstmConfig != null || value.ConvNetConfig != null))
            {
                value.PrepareForRt();
            }
            var ws = (value.IsWaveNet || value.IsLstm || value.IsConvNet) ? new NamWorkspace(value) : null;
            var partIr = value.CabIr is { Length: > 0 } ? PartitionedIr.Compile(value.CabIr) : null;
            Volatile.Write(ref _runtime, new ProfileRuntime(value, ws, partIr));
            Chain.RefreshNamBindings();
        }
    }

    public void SetModelLibrary(IEnumerable<ToneProfile> models)
    {
        var dict = new Dictionary<string, ToneProfile>(StringComparer.OrdinalIgnoreCase);
        if (models != null)
        {
            foreach (var m in models)
            {
                if (m != null && !string.IsNullOrWhiteSpace(m.Id))
                    dict[m.Id] = m;
            }
        }
        Volatile.Write(ref _modelRegistry, dict);
        Chain.RefreshNamBindings();
    }

    public ToneProfile? ResolveModel(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var dict = Volatile.Read(ref _modelRegistry);
            if (dict.TryGetValue(id.Trim(), out var found))
                return found;
        }
        return Volatile.Read(ref _runtime)?.Profile;
    }

    public string CustomCabIrName => Volatile.Read(ref _customCabIrName);
    public string? CustomCabIrPath => Volatile.Read(ref _customCabIrPath);
    public bool HasCustomCabIr => Volatile.Read(ref _customCabIrRev) != null;

    public (float[]? samples, int sampleRate, string name) GetCustomCabIrSnapshot()
    {
        var ir = Volatile.Read(ref _customCabIr);
        int sr = _customCabIrRate > 0 ? _customCabIrRate : (_sampleRate > 0 ? _sampleRate : 44100);
        string name = Volatile.Read(ref _customCabIrName);
        return (ir, sr, name);
    }

    public IReadOnlyCollection<ToneProfile> GetModelLibrarySnapshot() => Volatile.Read(ref _modelRegistry).Values;

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

    internal PartitionedIr? GetCustomOrProfilePartitionedIr()
    {
        var custom = Volatile.Read(ref _customCabPartIr);
        if (custom != null) return custom;
        return Volatile.Read(ref _runtime)?.CabPartIr;
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
        set => Volatile.Write(ref _outGainDb, Math.Clamp(value, -24f, 24f));
    }

    public float GateThresholdDb
    {
        get => Volatile.Read(ref _gateThreshDb);
        set => Volatile.Write(ref _gateThreshDb, Math.Clamp(value, -80f, -20f));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void RecordInputPeakRt(float pkL, float pkR)
    {
        if (pkL > Volatile.Read(ref _rtInPeakL)) Volatile.Write(ref _rtInPeakL, pkL);
        if (pkR > Volatile.Read(ref _rtInPeakR)) Volatile.Write(ref _rtInPeakR, pkR);
        if (pkL >= 0.98f || pkR >= 0.98f) Volatile.Write(ref _rtInClipFlag, 1);
    }

    public void TakeRtMeters(out float inPeakL, out float inPeakR, out bool inClip, out bool outClip)
    {
        inPeakL = Volatile.Read(ref _rtInPeakL);
        inPeakR = Volatile.Read(ref _rtInPeakR);
        Volatile.Write(ref _rtInPeakL, 0f);
        Volatile.Write(ref _rtInPeakR, 0f);
        inClip = Interlocked.Exchange(ref _rtInClipFlag, 0) == 1;
        outClip = Interlocked.Exchange(ref _rtOutClipFlag, 0) == 1;
    }

    /// <summary>외부 .wav 캐비넷 IR을 로드하여 현재 샘플레이트에 맞게 리샘플·위상정렬·정규화 후 원자적으로 교체한다.</summary>
    public (string name, int taps) LoadCustomCabIr(string wavPath)
    {
        if (!File.Exists(wavPath))
            throw new FileNotFoundException("캐비넷 IR 파일을 찾을 수 없습니다.", wavPath);

        float[] mono = WavFileCodec.ReadMonoFloat(wavPath, 2, out int srcRate);
        return LoadCustomCabIrFromSamples(mono, srcRate, Path.GetFileName(wavPath), wavPath);
    }

    /// <summary>메모리 내 모노 IR 샘플 배열을 현재 샘플레이트에 맞춰 리샘플·정규화 후 커스텀 IR로 원자적 적용한다.</summary>
    public (string name, int taps) LoadCustomCabIrFromSamples(
        float[] monoSamples,
        int sourceSampleRate,
        string displayName,
        string? backingPath = null)
    {
        if (monoSamples is null || monoSamples.Length < 8)
            throw new InvalidOperationException("IR 샘플이 비어 있거나 너무 짧습니다.");

        int srcRate = Math.Max(8000, sourceSampleRate);
        int dstRate = _sampleRate > 0 ? _sampleRate : 44100;
        float[] mono = srcRate != dstRate
            ? SincResampler.Resample(monoSamples, srcRate, dstRate)
            : (float[])monoSamples.Clone();

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

        var partIr = PartitionedIr.Compile(ir);
        string name = string.IsNullOrWhiteSpace(displayName) ? "CustomIR.wav" : Path.GetFileName(displayName);
        Volatile.Write(ref _customCabIrPath, backingPath);
        _customCabIrRate = dstRate;
        Volatile.Write(ref _customCabIr, ir);
        Volatile.Write(ref _customCabIrRev, rev);
        Volatile.Write(ref _customCabPartIr, partIr);
        Volatile.Write(ref _customCabIrName, name);
        return (name, len);
    }

    public void ClearCustomCabIr()
    {
        _customCabIrPath = null;
        _customCabIrRate = 0;
        Volatile.Write(ref _customCabIr, null);
        Volatile.Write(ref _customCabIrRev, null);
        Volatile.Write(ref _customCabPartIr, null);
        Volatile.Write(ref _customCabIrName, "");
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
        _monoCabR = new float[_maxFrames];
        _preHist = new float[64 + _maxFrames];
        _wsIn = new float[HistPad + _maxFrames];
        _wsOut = new float[_maxFrames];
        _h1 = new float[12 * (HistPad + _maxFrames)];
        _h2 = new float[12 * (HistPad + _maxFrames)];
        _cabHist = new float[2048 + _maxFrames];

        _singleCabConvolver.Prepare(_maxFrames);
        WhiteBox.Prepare(_maxFrames, _sampleRate);
        Chain.Prepare(_maxFrames, _sampleRate);
        Looper.Prepare(_sampleRate, maxSeconds: 60);
        ResetState();
    }

    public void ResetState()
    {
        if (_preHist.Length > 0) Array.Clear(_preHist);
        if (_wsIn.Length > 0) Array.Clear(_wsIn);
        if (_h1.Length > 0) Array.Clear(_h1);
        if (_h2.Length > 0) Array.Clear(_h2);
        if (_cabHist.Length > 0) Array.Clear(_cabHist);

        _singleCabConvolver.Reset();
        WhiteBox.Reset();
        Chain.Reset();
        Volatile.Read(ref _runtime)?.Nam?.Reset();

        _gateEnv = 0f;
        _gateGain = 1f;
        _gateOpen = true;
        _gateHoldSamples = 0;
        _compEnv = 0f;
        _dcX1L = _dcY1L = _dcX1R = _dcY1R = 0f;
        _smoothInitialized = false;
        Volatile.Write(ref _rtInPeakL, 0f);
        Volatile.Write(ref _rtInPeakR, 0f);
        Volatile.Write(ref _rtInClipFlag, 0);
        Volatile.Write(ref _rtOutClipFlag, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CaptureRawInputPeakRt(ReadOnlySpan<float> chL, ReadOnlySpan<float> chR, int frames)
    {
        float pkL = 0f, pkR = 0f;
        for (int i = 0; i < frames; i++)
        {
            float al = MathF.Abs(chL[i]);
            float ar = MathF.Abs(chR[i]);
            if (al > pkL) pkL = al;
            if (ar > pkR) pkR = ar;
        }
        RecordInputPeakRt(pkL, pkR);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinalizeStereoSampleRt(
        float outL,
        float outR,
        ref float preLimMax,
        out float finalL,
        out float finalR)
    {
        if (!float.IsFinite(outL)) outL = 0f;
        if (!float.IsFinite(outR)) outR = 0f;

        float yL = outL - _dcX1L + 0.9995f * _dcY1L;
        _dcX1L = outL;
        _dcY1L = yL;
        float yR = outR - _dcX1R + 0.9995f * _dcY1R;
        _dcX1R = outR;
        _dcY1R = yR;

        float aL = MathF.Abs(yL);
        float aR = MathF.Abs(yR);
        float m = aL > aR ? aL : aR;
        if (m > preLimMax) preLimMax = m;

        finalL = SoftKneeCeiling(yL);
        finalR = SoftKneeCeiling(yR);
    }

    /// <summary>
    /// 순수 NAM(WaveNet/LSTM/ConvNet) 추론 전용 진입점(게이트/리미터/DC블로커 없는 수치 동치 검증 및 오프라인 오디션용).
    /// </summary>
    public bool ProcessWaveNetRaw(ReadOnlySpan<float> input, Span<float> output)
    {
        var rt = Volatile.Read(ref _runtime);
        if (rt?.Nam is null || !rt.Nam.IsReady) return false;
        int n = Math.Min(input.Length, output.Length);
        rt.Nam.Process(input.Slice(0, n), output.Slice(0, n));
        return true;
    }

    /// <summary>ASIO 입력 콜백(RT 스레드)에서 호출. 할당/잠금 없음.</summary>
    public void Process(Span<float> chL, Span<float> chR, int frames)
    {
        var mode = (ToneMode)Volatile.Read(ref _mode);
        var rt = Volatile.Read(ref _runtime);
        var prof = rt?.Profile;
        var ws = rt?.Nam;
        var customCab = Volatile.Read(ref _customCabIrRev);

        if (frames <= 0) return;
        if (frames > _maxFrames) frames = _maxFrames;

        if (mode == ToneMode.Bypass)
        {
            CaptureRawInputPeakRt(chL, chR, frames);
            if (Looper.HasPendingOrActive)
                Looper.Process(chL, chR, frames, Chain.TempoBpm);
            return;
        }

        if (mode == ToneMode.SignalChain)
        {
            Chain.Process(chL, chR, frames, this);
            if (Looper.HasPendingOrActive)
                Looper.Process(chL, chR, frames, Chain.TempoBpm);
            float preLimMax = 0f;
            for (int i = 0; i < frames; i++)
            {
                FinalizeStereoSampleRt(chL[i], chR[i], ref preLimMax, out float finalL, out float finalR);
                chL[i] = finalL;
                chR[i] = finalR;
            }
            if (preLimMax >= 0.985f)
                Volatile.Write(ref _rtOutClipFlag, 1);
            return;
        }

        CaptureRawInputPeakRt(chL, chR, frames);

        if (prof is null && customCab is null && mode != ToneMode.WhiteBoxRig && mode != ToneMode.HybridPreampNam)
        {
            if (Looper.HasPendingOrActive)
                Looper.Process(chL, chR, frames, Chain.TempoBpm);
            return;
        }
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

        bool isNamReady = ws != null && ws.IsReady;
        bool runAmp = (mode == ToneMode.FullRig || mode == ToneMode.AmpHeadOnly) && prof != null;

        // 2) 조합형 하이브리드 라우팅 (WhiteBoxRig / HybridPreampNam / NAM / CabIrOnly)
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
            if (isNamReady)
            {
                ws!.Process(_monoWork.AsSpan(0, frames), _monoWork.AsSpan(0, frames));
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
        else if (runAmp && isNamReady)
        {
            if (WhiteBox.IsTsEnabled)
                WhiteBox.ProcessTs808Only(_monoWork.AsSpan(0, frames), frames);

            ApplySmoothedDrive(frames, targetDrive, paramAlpha);
            ws!.Process(_monoWork.AsSpan(0, frames), _monoWork.AsSpan(0, frames));

            // AmpHeadOnly(3)일 때만 NAM 뒤에 외부/내장 Cab IR 추가 컨볼루션
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
        float legacyPreLimMax = 0f;
        for (int i = 0; i < frames; i++)
        {
            _smoothOutGain += paramAlpha * (targetOutGain - _smoothOutGain);
            _smoothWet += paramAlpha * (targetWet - _smoothWet);

            float m = _monoWork[i] * _smoothOutGain;
            float wet = _smoothWet;
            float dry = 1f - wet;

            float outL = wet * m + dry * _dryL[i];
            float outR = wet * m + dry * _dryR[i];

            FinalizeStereoSampleRt(outL, outR, ref legacyPreLimMax, out float finalL, out float finalR);
            chL[i] = finalL;
            chR[i] = finalR;
        }
        if (legacyPreLimMax >= 0.985f)
            Volatile.Write(ref _rtOutClipFlag, 1);

        if (Looper.HasPendingOrActive)
        {
            Looper.Process(chL, chR, frames, Chain.TempoBpm);
            for (int i = 0; i < frames; i++)
            {
                chL[i] = SoftKneeCeiling(chL[i]);
                chR[i] = SoftKneeCeiling(chR[i]);
            }
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
        if (cabLen > 2048)
        {
            var part = GetCustomOrProfilePartitionedIr();
            if (part != null)
            {
                _monoWork.AsSpan(0, frames).CopyTo(_monoCabR.AsSpan(0, frames));
                _singleCabConvolver.ConvolveStereo(_monoWork.AsSpan(0, frames), _monoCabR.AsSpan(0, frames), frames, part);
                return;
            }
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftKneeCeiling(float y)
    {
        float ay = MathF.Abs(y);
        if (ay <= 0.92f) return y;
        float limited = 0.92f + 0.075f * FastTanhScalar((ay - 0.92f) / 0.075f);
        return y >= 0f ? limited : -limited;
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

