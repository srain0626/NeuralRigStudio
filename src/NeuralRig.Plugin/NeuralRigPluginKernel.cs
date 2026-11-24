using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Services;

namespace NeuralRigStudio.Plugin;

/// <summary>
/// VST3 / CLAP 공용 인스턴스 커널 (Milestone 5 / v2.0).
/// - 인스턴스별 독립 ToneProcessor 보유 (멀티 트랙 100% 스레드 세이프)
/// - 32개 표준 파라미터의 샘플 정확도(Sample-Accurate) 무할당 오토메이션 적용
/// - .nrig 아카이브 기반 DAW State 저장/복원 (NAM 가중치 + 커스텀 IR + 8씬 완비)
/// </summary>
public sealed class NeuralRigPluginKernel : IDisposable
{
    private const uint StateMagic = 0x3253524E; // "NRS2" LE
    private const int MaxEventsPerBlock = 256;

    private sealed class PluginStateEnvelope
    {
        public float InputGainDb { get; set; }
        public float OutputGainDb { get; set; }
        public float GateThresholdDb { get; set; } = -56f;
        public float DriveMultiplier { get; set; } = 1.0f;
        public float WetMix { get; set; } = 1.0f;
        public float TempoBpm { get; set; } = 120f;
        public int ActiveScene { get; set; }
        public float[] NormalizedParams { get; set; } = Array.Empty<float>();
    }

    private readonly ToneProcessor _processor = new() { Mode = ToneMode.SignalChain };
    private readonly float[] _paramNorm = new float[DawParameterCatalog.ParameterCount];
    private readonly DawParamEvent[] _pendingEvents = new DawParamEvent[MaxEventsPerBlock];
    private readonly DawParamEvent[] _rtScratchEvents = new DawParamEvent[MaxEventsPerBlock];
    private readonly object _eventLock = new();
    private int _pendingEventCount;

    private int _sampleRate = 48000;
    private int _maxFrames = 1024;
    private float _inputGainDb;
    private float _outputGainDb;
    private float _lastHostBpm = -1f;
    private string? _instanceSandboxDir;

    public ToneProcessor Processor => _processor;
    public int SampleRate => _sampleRate;
    public int MaxFrames => _maxFrames;
    public bool IsActive { get; private set; }

    public NeuralRigPluginKernel()
    {
        for (uint i = 0; i < DawParameterCatalog.ParameterCount; i++)
            _paramNorm[i] = DawParameterCatalog.PlainToNormalized(i, DawParameterCatalog.All[i].DefaultValue);

        var initChain = AntigravityToneArchitect.EnsureScenes(new SignalChainConfigDto());
        _processor.Chain.UpdateChain(initChain);
        SyncParamsFromChain(initChain);
    }

    public void Activate(int sampleRate, int maxBlockFrames)
    {
        _sampleRate = Math.Clamp(sampleRate, 8000, 192000);
        _maxFrames = Math.Clamp(maxBlockFrames, 32, 8192);
        _processor.Prepare(_maxFrames, _sampleRate);
        ApplyAllCachedParamsToRt();
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void EnqueueParamChange(uint paramId, float normalizedValue, int sampleOffset = 0)
    {
        if (paramId >= DawParameterCatalog.ParameterCount) return;
        float n = Math.Clamp(normalizedValue, 0f, 1f);
        Volatile.Write(ref _paramNorm[paramId], n);

        lock (_eventLock)
        {
            if (_pendingEventCount < MaxEventsPerBlock)
            {
                _pendingEvents[_pendingEventCount++] = new DawParamEvent
                {
                    SampleOffset = Math.Max(0, sampleOffset),
                    ParamId = paramId,
                    NormalizedValue = n,
                };
            }
        }
    }

    public float GetParamNormalized(uint paramId)
        => paramId < DawParameterCatalog.ParameterCount ? Volatile.Read(ref _paramNorm[paramId]) : 0f;

    /// <summary>
    /// RT 오디오 콜백 (힙 할당 0 B).
    /// 호스트 템포 BPM 동기화 + 샘플 오프셋 분할 오토메이션 + ToneProcessor 2x8 시그널 체인 처리.
    /// </summary>
    public void ProcessRt(Span<float> chL, Span<float> chR, int frames, double hostTempoBpm = 0.0)
    {
        if (frames <= 0) return;
        int n = Math.Min(frames, Math.Min(chL.Length, chR.Length));
        if (n <= 0) return;

        // 1) 호스트 Transport BPM 변경 시 무할당 동기화
        if (hostTempoBpm >= 40.0 && hostTempoBpm <= 280.0)
        {
            float hBpm = (float)hostTempoBpm;
            if (MathF.Abs(hBpm - _lastHostBpm) > 0.05f)
            {
                _lastHostBpm = hBpm;
                _processor.Chain.SetTempoBpmRt(hBpm);
                Volatile.Write(ref _paramNorm[(int)DawParamId.TempoBpm],
                    DawParameterCatalog.PlainToNormalized((uint)DawParamId.TempoBpm, hBpm));
            }
        }

        // 2) 큐에 쌓인 샘플 정확도 오토메이션 이벤트를 무할당 복사
        int evCount = 0;
        if (Volatile.Read(ref _pendingEventCount) > 0)
        {
            lock (_eventLock)
            {
                evCount = Math.Min(_pendingEventCount, _rtScratchEvents.Length);
                for (int i = 0; i < evCount; i++)
                    _rtScratchEvents[i] = _pendingEvents[i];
                _pendingEventCount = 0;
            }
        }

        if (evCount <= 0)
        {
            _processor.Process(chL.Slice(0, n), chR.Slice(0, n), n);
            return;
        }

        // 3) 샘플 오프셋 오름차순 삽입 정렬 (할당 0 B) 후 서브 블록 분할 처리
        for (int i = 1; i < evCount; i++)
        {
            var key = _rtScratchEvents[i];
            int j = i - 1;
            while (j >= 0 && _rtScratchEvents[j].SampleOffset > key.SampleOffset)
            {
                _rtScratchEvents[j + 1] = _rtScratchEvents[j];
                j--;
            }
            _rtScratchEvents[j + 1] = key;
        }

        int cursor = 0;
        int evIdx = 0;
        while (cursor < n)
        {
            while (evIdx < evCount && _rtScratchEvents[evIdx].SampleOffset <= cursor)
            {
                ApplyParamEventRt(_rtScratchEvents[evIdx]);
                evIdx++;
            }

            int nextOffset = evIdx < evCount ? Math.Clamp(_rtScratchEvents[evIdx].SampleOffset, cursor, n) : n;
            int subFrames = nextOffset - cursor;
            if (subFrames <= 0)
            {
                if (evIdx < evCount)
                {
                    ApplyParamEventRt(_rtScratchEvents[evIdx]);
                    evIdx++;
                    continue;
                }
                break;
            }

            _processor.Process(chL.Slice(cursor, subFrames), chR.Slice(cursor, subFrames), subFrames);
            cursor += subFrames;
        }

        while (evIdx < evCount)
        {
            ApplyParamEventRt(_rtScratchEvents[evIdx]);
            evIdx++;
        }
    }

    private void ApplyParamEventRt(in DawParamEvent ev)
    {
        float plain = DawParameterCatalog.NormalizedToPlain(ev.ParamId, ev.NormalizedValue);
        switch ((DawParamId)ev.ParamId)
        {
            case DawParamId.InputGainDb:
                _inputGainDb = plain;
                _processor.Chain.SetGlobalParamsRt(inputGainDb: plain);
                break;
            case DawParamId.OutputGainDb:
                _outputGainDb = plain;
                _processor.Chain.SetGlobalParamsRt(outputGainDb: plain);
                break;
            case DawParamId.GateThresholdDb:
                _processor.GateThresholdDb = plain;
                break;
            case DawParamId.DriveMultiplier:
                _processor.DriveMultiplier = plain;
                break;
            case DawParamId.WetMix:
                _processor.WetMix = plain / 100f;
                break;
            case DawParamId.ActiveScene:
            {
                int sceneIdx = Math.Clamp((int)MathF.Round(plain), 0, 7);
                if (_processor.Chain.SwitchSceneRt(sceneIdx))
                {
                    // RT에서 씬 전환 후 슬롯 바이패스 및 Amp 매크로 파라미터 정규화 캐시 동기화
                    for (int s = 0; s < 16; s++)
                        Volatile.Write(ref _paramNorm[(int)DawParamId.Slot01Bypass + s], _processor.Chain.GetSlotBypassRt(s) ? 1f : 0f);
                    Volatile.Write(ref _paramNorm[(int)DawParamId.ParallelMix], _processor.Chain.ParallelMix);
                }
                break;
            }
            case DawParamId.TempoBpm:
                _processor.Chain.SetTempoBpmRt(plain);
                break;
            case DawParamId.RoutingMode:
                _processor.Chain.SetGlobalParamsRt(routing: plain >= 0.5f ? ChainRoutingMode.ParallelSplit : ChainRoutingMode.Serial);
                break;
            case DawParamId.ParallelMix:
                _processor.Chain.SetGlobalParamsRt(parallelMix: plain / 100f);
                break;
            case >= DawParamId.Slot01Bypass and <= DawParamId.Slot16Bypass:
            {
                int slotIdx = (int)(ev.ParamId - (uint)DawParamId.Slot01Bypass);
                _processor.Chain.SetSlotBypassRt(slotIdx, plain >= 0.5f);
                break;
            }
            case DawParamId.AmpGain:
                _processor.Chain.SetPrimaryAmpParamRt(0, plain);
                break;
            case DawParamId.AmpBass:
                _processor.Chain.SetPrimaryAmpParamRt(1, plain);
                break;
            case DawParamId.AmpMid:
                _processor.Chain.SetPrimaryAmpParamRt(2, plain);
                break;
            case DawParamId.AmpTreble:
                _processor.Chain.SetPrimaryAmpParamRt(3, plain);
                break;
            case DawParamId.AmpMaster:
                _processor.Chain.SetPrimaryAmpParamRt(4, plain);
                break;
            case DawParamId.AmpSag:
                _processor.Chain.SetPrimaryAmpParamRt(5, plain);
                break;
            case DawParamId.AmpPresence:
                _processor.Chain.SetPrimaryAmpParamRt(6, plain);
                break;
        }
    }

    private void ApplyAllCachedParamsToRt()
    {
        for (uint i = 0; i < DawParameterCatalog.ParameterCount; i++)
        {
            if (i == (uint)DawParamId.ActiveScene) continue;
            ApplyParamEventRt(new DawParamEvent
            {
                ParamId = i,
                NormalizedValue = Volatile.Read(ref _paramNorm[i]),
            });
        }
    }

    public void LoadChainConfig(SignalChainConfigDto chain)
    {
        var ready = AntigravityToneArchitect.EnsureScenes((chain ?? new SignalChainConfigDto()).Clone());
        _processor.Chain.UpdateChain(ready);
        _processor.Mode = ToneMode.SignalChain;
        SyncParamsFromChain(ready);
    }

    public void SyncParamsFromChain(SignalChainConfigDto chain)
    {
        _inputGainDb = chain.InputGainDb;
        _outputGainDb = chain.OutputGainDb;
        _paramNorm[(int)DawParamId.InputGainDb] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.InputGainDb, chain.InputGainDb);
        _paramNorm[(int)DawParamId.OutputGainDb] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.OutputGainDb, chain.OutputGainDb);
        _paramNorm[(int)DawParamId.GateThresholdDb] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.GateThresholdDb, _processor.GateThresholdDb);
        _paramNorm[(int)DawParamId.DriveMultiplier] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.DriveMultiplier, _processor.DriveMultiplier);
        _paramNorm[(int)DawParamId.WetMix] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.WetMix, _processor.WetMix * 100f);
        _paramNorm[(int)DawParamId.ActiveScene] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.ActiveScene, chain.ActiveScene);
        _paramNorm[(int)DawParamId.TempoBpm] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.TempoBpm, chain.TempoBpm);
        _paramNorm[(int)DawParamId.RoutingMode] = chain.RoutingMode >= 1 ? 1f : 0f;
        _paramNorm[(int)DawParamId.ParallelMix] = Math.Clamp(chain.ParallelMix, 0f, 1f);

        for (int i = 0; i < 16 && i < chain.Slots.Count; i++)
            _paramNorm[(int)DawParamId.Slot01Bypass + i] = chain.Slots[i].Enabled ? 1f : 0f;

        for (int m = 0; m < 7; m++)
        {
            if (_processor.Chain.TryGetPrimaryAmpParam(m, out float val))
                _paramNorm[(int)DawParamId.AmpGain + m] = DawParameterCatalog.PlainToNormalized((uint)DawParamId.AmpGain + (uint)m, val);
        }
    }

    /// <summary>DAW 프로젝트 저장용: 모든 실시간 파라미터 + .nrig 번들(체인+8씬+NAM가중치+IR)을 단일 바이너리 청크로 직렬화.</summary>
    public byte[] SaveStateBytes()
    {
        var liveChain = _processor.Chain.ExportLiveStateSnapshot();
        var (nrigBytes, _) = NrigBundleService.ExportBundleToBytes(
            liveChain,
            _processor,
            _processor.GetModelLibrarySnapshot(),
            author: "DAW State",
            description: "Embedded NeuralRig Studio VST3/CLAP Project State",
            sampleRate: _sampleRate);

        var env = new PluginStateEnvelope
        {
            InputGainDb = liveChain.InputGainDb,
            OutputGainDb = liveChain.OutputGainDb,
            GateThresholdDb = _processor.GateThresholdDb,
            DriveMultiplier = _processor.DriveMultiplier,
            WetMix = _processor.WetMix,
            TempoBpm = liveChain.TempoBpm,
            ActiveScene = liveChain.ActiveScene,
            NormalizedParams = _paramNorm.ToArray(),
        };
        byte[] metaBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(env));

        byte[] output = new byte[12 + metaBytes.Length + nrigBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), StateMagic);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4, 4), metaBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8, 4), nrigBytes.Length);
        Buffer.BlockCopy(metaBytes, 0, output, 12, metaBytes.Length);
        Buffer.BlockCopy(nrigBytes, 0, output, 12 + metaBytes.Length, nrigBytes.Length);
        return output;
    }

    public bool LoadStateBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4)) != StateMagic)
        {
            // Raw .nrig ZIP 직접 로드도 허용 (드래그/프리셋 임포트 호환)
            if (data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B)
            {
                using var zipMs = new MemoryStream(data.ToArray(), writable: false);
                string root = EnsureSandboxDir();
                var res = NrigBundleService.ImportBundle(zipMs, _processor, Path.Combine(root, "models"), Path.Combine(root, "irs"));
                SyncParamsFromChain(res.SignalChain);
                return true;
            }
            return false;
        }

        int metaLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        int nrigLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
        if (metaLen <= 0 || nrigLen <= 0 || 12 + metaLen + nrigLen > data.Length)
            return false;

        var env = JsonSerializer.Deserialize<PluginStateEnvelope>(data.Slice(12, metaLen));
        byte[] nrig = data.Slice(12 + metaLen, nrigLen).ToArray();

        string sbRoot = EnsureSandboxDir();
        using var ms = new MemoryStream(nrig, writable: false);
        var imported = NrigBundleService.ImportBundle(
            ms,
            _processor,
            Path.Combine(sbRoot, "models"),
            Path.Combine(sbRoot, "irs"));

        if (env != null)
        {
            _processor.GateThresholdDb = env.GateThresholdDb;
            _processor.DriveMultiplier = env.DriveMultiplier;
            _processor.WetMix = env.WetMix;
            if (env.NormalizedParams is { Length: > 0 })
            {
                int copyLen = Math.Min(_paramNorm.Length, env.NormalizedParams.Length);
                Array.Copy(env.NormalizedParams, _paramNorm, copyLen);
            }
            else
            {
                SyncParamsFromChain(imported.SignalChain);
            }
            ApplyAllCachedParamsToRt();
        }
        else
        {
            SyncParamsFromChain(imported.SignalChain);
        }
        return true;
    }

    private string EnsureSandboxDir()
    {
        if (string.IsNullOrEmpty(_instanceSandboxDir))
        {
            _instanceSandboxDir = Path.Combine(Path.GetTempPath(), $"nrs_plugin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_instanceSandboxDir);
        }
        return _instanceSandboxDir;
    }

    public void Dispose()
    {
        IsActive = false;
        if (!string.IsNullOrEmpty(_instanceSandboxDir) && Directory.Exists(_instanceSandboxDir))
        {
            try { Directory.Delete(_instanceSandboxDir, recursive: true); } catch { }
        }
    }
}
