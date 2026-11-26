using System.Text;
using System.Text.Json;
using NeuralRigStudio.Audio;

namespace NeuralRigStudio.Plugin;

public readonly record struct PluginParamChangeEvent(uint ParamId, float NormValue, int SampleOffset);

/// <summary>
/// NeuralRig Studio VST3 / CLAP 공용 실시간 DSP 커널 (NativeAOT 호환 · 오디오 콜백 할당 0 B).
/// </summary>
public sealed class NeuralRigPluginKernel : IAudioTelemetrySource, IDisposable
{
    private const int TelemetryRingCap = 8192;
    private const int TelemetryRingMask = TelemetryRingCap - 1;
    private const int MaxRtEvents = 512;
    private static readonly byte[] StateMagic = Encoding.ASCII.GetBytes("NRIGST01");

    private readonly ToneProcessor _processor = new();
    private readonly DualStreamRecorder _recorder = new();
    private readonly object _metaLock = new();

    private readonly float[] _paramNorm = new float[DawParameterCatalog.TotalParameterCount];
    private readonly float[] _paramMin = new float[DawParameterCatalog.TotalParameterCount];
    private readonly float[] _paramMax = new float[DawParameterCatalog.TotalParameterCount];
    private readonly int[] _paramStep = new int[DawParameterCatalog.TotalParameterCount];
    private readonly string?[] _slotModelIds = new string?[DawParameterCatalog.MatrixSlotCount];
    private int _primaryAmpSlot = -1;

    private readonly float[] _wetRing = new float[TelemetryRingCap];
    private readonly float[] _dryRing = new float[TelemetryRingCap];
    private int _wetRingPos;
    private int _dryRingPos;
    private float _peakL;
    private float _peakR;
    private int _clipFlag;

    private readonly PluginParamChangeEvent[] _rtEvents = new PluginParamChangeEvent[MaxRtEvents];
    private readonly PluginParamChangeEvent[] _rtScratch = new PluginParamChangeEvent[MaxRtEvents];
    private int _rtEventCount;
    private int _evSpin;

    private long _dawDirty0;
    private long _dawDirty1;
    private long _dawDirty2;

    private int _isActive;
    private int _sampleRate = 48000;
    private int _maxBlockSize = 512;
    private int _hostBpmSync = 1;
    private int _tunerMute;
    private double _hostBpm = 120.0;
    private float _lastAppliedHostBpm = -1f;

    public ToneProcessor Processor => _processor;
    public DualStreamRecorder Recorder => _recorder;
    public string HostFormat { get; set; } = "VST3";
    public bool IsRunning => Volatile.Read(ref _isActive) == 1;
    public int EffectiveSampleRate => Volatile.Read(ref _sampleRate);
    public int MaxBlockSize => Volatile.Read(ref _maxBlockSize);

    public bool HostBpmSync
    {
        get => Volatile.Read(ref _hostBpmSync) == 1;
        set => Volatile.Write(ref _hostBpmSync, value ? 1 : 0);
    }

    public double HostBpm => Volatile.Read(ref _hostBpm);

    public bool TunerMute
    {
        get => Volatile.Read(ref _tunerMute) == 1;
        set => Volatile.Write(ref _tunerMute, value ? 1 : 0);
    }

    public NeuralRigPluginKernel()
    {
        for (int i = 0; i < DawParameterCatalog.All.Length; i++)
        {
            var d = DawParameterCatalog.All[i];
            _paramMin[i] = d.MinValue;
            _paramMax[i] = d.MaxValue;
            _paramStep[i] = d.StepCount;
            _paramNorm[i] = DawParameterCatalog.PlainToNormalizedRange(d.DefaultValue, d.MinValue, d.MaxValue);
        }

        _processor.Prepare(512, 48000);
        _recorder.Prepare(48000);
        _processor.Mode = ToneMode.SignalChain;
        LoadChainConfig(new SignalChainConfigDto());
    }

    public void Activate(int sampleRate, int maxBlockSize)
    {
        int sr = Math.Clamp(sampleRate, 44100, 192000);
        int maxFrames = Math.Clamp(maxBlockSize, 32, 8192);
        Volatile.Write(ref _sampleRate, sr);
        Volatile.Write(ref _maxBlockSize, maxFrames);
        _processor.Prepare(maxFrames, sr);
        _recorder.Prepare(sr);
        _lastAppliedHostBpm = -1f;
        Volatile.Write(ref _isActive, 1);
    }

    public void Deactivate()
    {
        Volatile.Write(ref _isActive, 0);
        AcquireEvLock();
        _rtEventCount = 0;
        ReleaseEvLock();
    }

    public void LoadChainConfig(SignalChainConfigDto dto, Func<string?, ToneProfile?>? namResolver = null)
    {
        var clean = (dto ?? new SignalChainConfigDto()).Clone();
        _processor.Chain.UpdateChain(clean, namResolver);
        _processor.Mode = ToneMode.SignalChain;
        SyncParamsFromEngine();
    }

    public void RefreshMetadataFromChain(SignalChainConfigDto chain)
    {
        lock (_metaLock)
        {
            for (int s = 0; s < DawParameterCatalog.MatrixSlotCount; s++)
            {
                string? modelId = s < chain.Slots.Count ? chain.Slots[s].ModelId : null;
                _slotModelIds[s] = modelId;
                for (int k = 0; k < DawParameterCatalog.KnobsPerSlot; k++)
                {
                    uint id = DawParameterCatalog.SlotKnobParamId(s, k);
                    var meta = DawParameterCatalog.GetModelParamMeta(modelId, k);
                    Volatile.Write(ref _paramMin[id], meta.MinValue);
                    Volatile.Write(ref _paramMax[id], meta.MaxValue);
                    Volatile.Write(ref _paramStep[id], meta.StepCount);
                }
            }
        }

        Volatile.Write(ref _primaryAmpSlot, _processor.Chain.GetPrimaryAmpSlotIndex());
    }

    public void SyncParamsFromEngine()
        => SyncParamsFromSnapshot(_processor.Chain.ExportLiveStateSnapshot());

    private void SyncParamsFromSnapshot(SignalChainConfigDto chain)
    {
        RefreshMetadataFromChain(chain);

        WriteNorm((uint)DawParamId.InputGainDb, DawParameterCatalog.PlainToNormalized((uint)DawParamId.InputGainDb, chain.InputGainDb));
        WriteNorm((uint)DawParamId.OutputGainDb, DawParameterCatalog.PlainToNormalized((uint)DawParamId.OutputGainDb, chain.OutputGainDb));
        WriteNorm((uint)DawParamId.GateThresholdDb, DawParameterCatalog.PlainToNormalized((uint)DawParamId.GateThresholdDb, _processor.GateThresholdDb));
        WriteNorm((uint)DawParamId.DriveMultiplier, DawParameterCatalog.PlainToNormalized((uint)DawParamId.DriveMultiplier, _processor.DriveMultiplier));
        WriteNorm((uint)DawParamId.WetMix, DawParameterCatalog.PlainToNormalized((uint)DawParamId.WetMix, _processor.WetMix * 100f));
        WriteNorm((uint)DawParamId.ActiveScene, DawParameterCatalog.PlainToNormalized((uint)DawParamId.ActiveScene, chain.ActiveScene));
        WriteNorm((uint)DawParamId.TempoBpm, DawParameterCatalog.PlainToNormalized((uint)DawParamId.TempoBpm, chain.TempoBpm));
        WriteNorm((uint)DawParamId.RoutingMode, chain.RoutingMode >= 1 ? 1f : 0f);
        WriteNorm((uint)DawParamId.ParallelMix, Math.Clamp(chain.ParallelMix, 0f, 1f));

        RefreshAmpMacroParamsFromEngineRt();
        RefreshSlotParamsFromEngineRt();
    }

    private void RefreshAmpMacroParamsFromEngineRt()
    {
        for (int m = 0; m < 7; m++)
        {
            if (_processor.Chain.TryGetPrimaryAmpParam(m, out float ampVal))
                WriteNorm((uint)((int)DawParamId.AmpGain + m), DawParameterCatalog.PlainToNormalized((uint)((int)DawParamId.AmpGain + m), ampVal));
        }
    }

    private void RefreshSlotParamsFromEngineRt()
    {
        for (int s = 0; s < DawParameterCatalog.MatrixSlotCount; s++)
        {
            uint bypassId = DawParameterCatalog.SlotBypassParamId(s);
            WriteNorm(bypassId, _processor.Chain.GetSlotBypassRt(s) ? 1f : 0f);

            for (int k = 0; k < DawParameterCatalog.KnobsPerSlot; k++)
            {
                if (_processor.Chain.TryGetSlotParam(s, k, out float val))
                {
                    uint pId = DawParameterCatalog.SlotKnobParamId(s, k);
                    float min = Volatile.Read(ref _paramMin[pId]);
                    float max = Volatile.Read(ref _paramMax[pId]);
                    WriteNorm(pId, DawParameterCatalog.PlainToNormalizedRange(val, min, max));
                }
            }
        }
    }

    private void WriteNorm(uint id, float norm01)
    {
        if (id < DawParameterCatalog.TotalParameterCount)
            Volatile.Write(ref _paramNorm[id], Math.Clamp(norm01, 0f, 1f));
    }

    public float GetParamNormalized(uint paramId)
        => paramId < DawParameterCatalog.TotalParameterCount ? Volatile.Read(ref _paramNorm[paramId]) : 0f;

    public float NormalizedToPlainLive(uint paramId, float norm01)
    {
        if (paramId >= DawParameterCatalog.TotalParameterCount) return 0f;
        float min = Volatile.Read(ref _paramMin[paramId]);
        float max = Volatile.Read(ref _paramMax[paramId]);
        int steps = Volatile.Read(ref _paramStep[paramId]);
        return DawParameterCatalog.NormalizedToPlainRange(norm01, min, max, steps);
    }

    public float PlainToNormalizedLive(uint paramId, float plain)
    {
        if (paramId >= DawParameterCatalog.TotalParameterCount) return 0f;
        float min = Volatile.Read(ref _paramMin[paramId]);
        float max = Volatile.Read(ref _paramMax[paramId]);
        return DawParameterCatalog.PlainToNormalizedRange(plain, min, max);
    }

    public DawParamDescriptor GetParamDisplayInfo(uint paramId, out string? onLabel, out string? offLabel)
    {
        onLabel = null;
        offLabel = null;
        if (paramId >= DawParameterCatalog.TotalParameterCount)
            return new(paramId, "Unknown", "Unknown", "", 0f, 1f, 0f, 0);

        if (DawParameterCatalog.TryDecodeSlotParam(paramId, out int slotIdx, out bool isBypass, out int knobIdx))
        {
            string? modelId;
            lock (_metaLock)
                modelId = _slotModelIds[slotIdx];

            string mod = string.IsNullOrWhiteSpace(modelId) ? $"Slot {slotIdx + 1:D2}" : $"Slot {slotIdx + 1:D2} ({modelId})";
            if (isBypass)
                return new(paramId, $"Slot {slotIdx + 1:D2} Active", mod, "", 0f, 1f, 1f, 1);

            var meta = DawParameterCatalog.GetModelParamMeta(modelId, knobIdx);
            onLabel = meta.OnLabel;
            offLabel = meta.OffLabel;
            return new(
                paramId,
                $"S{slotIdx + 1:D2} {meta.Label}",
                mod,
                meta.Units,
                meta.MinValue,
                meta.MaxValue,
                meta.DefaultValue,
                meta.StepCount);
        }

        return DawParameterCatalog.All[paramId];
    }

    public string FormatParamValue(uint paramId, float norm01)
    {
        var desc = GetParamDisplayInfo(paramId, out string? onLabel, out string? offLabel);
        float plain = NormalizedToPlainLive(paramId, norm01);
        return DawParameterCatalog.FormatPlainValue(paramId, plain, desc.Unit, onLabel, offLabel);
    }

    public int GetLatencySamples() => _processor.Chain.GetLatencySamples();

    public int GetTailSamples() => _processor.Chain.GetTailSamples();

    public bool TryResolveMidiControllerParam(int ccNumber, out uint paramId)
    {
        if (ccNumber is 1 or 4 or 11)
        {
            int exprSlot = _processor.Chain.FindExpressionTargetSlot();
            paramId = exprSlot >= 0
                ? DawParameterCatalog.SlotKnobParamId(exprSlot, 0)
                : (uint)DawParamId.DriveMultiplier;
            return true;
        }
        if (ccNumber == 7)
        {
            paramId = (uint)DawParamId.OutputGainDb;
            return true;
        }
        if (ccNumber == 130)
        {
            paramId = (uint)DawParamId.ActiveScene;
            return true;
        }
        paramId = 0;
        return false;
    }

    public void EnqueueMidiEventRt(byte status, byte data1, byte data2, int sampleOffset = 0)
    {
        byte msgType = (byte)(status & 0xF0);
        if (msgType == 0xB0)
        {
            if (TryResolveMidiControllerParam(data1 & 0x7F, out uint paramId))
                EnqueueParamChange(paramId, Math.Clamp((data2 & 0x7F) / 127f, 0f, 1f), sampleOffset);
        }
        else if (msgType == 0xC0)
        {
            EnqueueParamChange((uint)DawParamId.ActiveScene, Math.Clamp((data1 & 7) / 7f, 0f, 1f), sampleOffset);
        }
    }

    public void EnqueueParamChange(uint paramId, float norm01, int sampleOffset = 0)
    {
        if (paramId >= DawParameterCatalog.TotalParameterCount) return;
        float clamped = Math.Clamp(norm01, 0f, 1f);
        Volatile.Write(ref _paramNorm[paramId], clamped);
        MarkDawDirty((int)paramId);

        AcquireEvLock();
        if (_rtEventCount < MaxRtEvents)
            _rtEvents[_rtEventCount++] = new(paramId, clamped, Math.Max(0, sampleOffset));
        ReleaseEvLock();
    }

    public void SetParamFromUi(uint paramId, float norm01)
    {
        if (paramId >= DawParameterCatalog.TotalParameterCount) return;
        float clamped = Math.Clamp(norm01, 0f, 1f);
        Volatile.Write(ref _paramNorm[paramId], clamped);
        ApplyParamRt(new(paramId, clamped, 0));
    }

    public bool TryConsumeDawParamSync()
    {
        long m0 = Interlocked.Exchange(ref _dawDirty0, 0L);
        long m1 = Interlocked.Exchange(ref _dawDirty1, 0L);
        long m2 = Interlocked.Exchange(ref _dawDirty2, 0L);
        return (m0 | m1 | m2) != 0L;
    }

    private void MarkDawDirty(int paramIdx)
    {
        if (paramIdx < 64)
            Interlocked.Or(ref _dawDirty0, 1L << paramIdx);
        else if (paramIdx < 128)
            Interlocked.Or(ref _dawDirty1, 1L << (paramIdx - 64));
        else if (paramIdx < 160)
            Interlocked.Or(ref _dawDirty2, 1L << (paramIdx - 128));
    }

    private void AcquireEvLock()
    {
        while (Interlocked.CompareExchange(ref _evSpin, 1, 0) != 0)
            Thread.SpinWait(1);
    }

    private void ReleaseEvLock() => Volatile.Write(ref _evSpin, 0);

    /// <summary>실시간 오디오 콜백 (힙 할당 0 B).</summary>
    public void ProcessRt(Span<float> left, Span<float> right, int frames, double hostTempoBpm = 0.0)
        => ProcessRt(left, right, frames, hostTempoBpm, isPlaying: false, ppqPosition: 0.0, hasPpq: false);

    public void ProcessRt(
        Span<float> left,
        Span<float> right,
        int frames,
        double hostTempoBpm,
        bool isPlaying,
        double ppqPosition,
        bool hasPpq)
    {
        if (frames <= 0) return;
        int n = Math.Min(frames, Math.Min(left.Length, right.Length));
        Span<float> sl = left.Slice(0, n);
        Span<float> sr = right.Slice(0, n);

        if (hostTempoBpm >= 40.0 && hostTempoBpm <= 280.0)
        {
            Volatile.Write(ref _hostBpm, hostTempoBpm);
            if (HostBpmSync)
            {
                float fBpm = (float)hostTempoBpm;
                if (MathF.Abs(fBpm - _lastAppliedHostBpm) > 0.05f)
                {
                    _lastAppliedHostBpm = fBpm;
                    _processor.Chain.SetTempoBpmRt(fBpm);
                    WriteNorm((uint)DawParamId.TempoBpm, DawParameterCatalog.PlainToNormalized((uint)DawParamId.TempoBpm, fBpm));
                    MarkDawDirty((int)DawParamId.TempoBpm);
                }
            }
        }

        _processor.Chain.SyncHostTransportRt(isPlaying, ppqPosition, hasPpq, n);

        int evCount;
        AcquireEvLock();
        evCount = _rtEventCount;
        for (int i = 0; i < evCount; i++)
            _rtScratch[i] = _rtEvents[i];
        _rtEventCount = 0;
        ReleaseEvLock();

        if (evCount <= 0)
        {
            ProcessAudioChunkRt(sl, sr);
            return;
        }

        if (evCount == 1)
        {
            int off = Math.Clamp(_rtScratch[0].SampleOffset, 0, n);
            if (off > 0)
                ProcessAudioChunkRt(sl.Slice(0, off), sr.Slice(0, off));
            ApplyParamRt(_rtScratch[0]);
            if (off < n)
                ProcessAudioChunkRt(sl.Slice(off), sr.Slice(off));
            return;
        }

        // Insertion sort small batch on RT thread (0 B alloc)
        for (int i = 1; i < evCount; i++)
        {
            var key = _rtScratch[i];
            int j = i - 1;
            while (j >= 0 && _rtScratch[j].SampleOffset > key.SampleOffset)
            {
                _rtScratch[j + 1] = _rtScratch[j];
                j--;
            }
            _rtScratch[j + 1] = key;
        }

        int cursor = 0;
        for (int i = 0; i < evCount; i++)
        {
            int off = Math.Clamp(_rtScratch[i].SampleOffset, cursor, n);
            if (off > cursor)
            {
                ProcessAudioChunkRt(sl.Slice(cursor, off - cursor), sr.Slice(cursor, off - cursor));
                cursor = off;
            }
            ApplyParamRt(_rtScratch[i]);
        }

        if (cursor < n)
            ProcessAudioChunkRt(sl.Slice(cursor, n - cursor), sr.Slice(cursor, n - cursor));
    }

    private void ProcessAudioChunkRt(Span<float> sl, Span<float> sr)
    {
        int len = sl.Length;
        if (len <= 0) return;

        _recorder.InjectReampDryRt(sl, sr, len);
        _recorder.CaptureDryRt(sl, sr, len);

        int dPos = _dryRingPos;
        for (int i = 0; i < len; i++)
        {
            _dryRing[dPos] = 0.5f * (sl[i] + sr[i]);
            dPos = (dPos + 1) & TelemetryRingMask;
        }
        Volatile.Write(ref _dryRingPos, dPos);

        _processor.Process(sl, sr, len);
        _recorder.CaptureWetRt(sl, sr, len);

        if (Volatile.Read(ref _tunerMute) == 1)
        {
            sl.Clear();
            sr.Clear();
        }

        float pL = Volatile.Read(ref _peakL);
        float pR = Volatile.Read(ref _peakR);
        bool clipped = Volatile.Read(ref _clipFlag) == 1;

        int wPos = _wetRingPos;
        for (int i = 0; i < len; i++)
        {
            float l = sl[i];
            float r = sr[i];
            if (!float.IsFinite(l)) l = 0f;
            if (!float.IsFinite(r)) r = 0f;
            float al = MathF.Abs(l);
            float ar = MathF.Abs(r);
            if (al > pL) pL = al;
            if (ar > pR) pR = ar;
            if (al >= 0.99f || ar >= 0.99f) clipped = true;
            _wetRing[wPos] = 0.5f * (l + r);
            wPos = (wPos + 1) & TelemetryRingMask;
        }

        Volatile.Write(ref _wetRingPos, wPos);
        Volatile.Write(ref _peakL, pL);
        Volatile.Write(ref _peakR, pR);
        if (clipped) Volatile.Write(ref _clipFlag, 1);
    }

    public void FlushPendingParamsNoAudio()
    {
        AcquireEvLock();
        int evCount = _rtEventCount;
        for (int i = 0; i < evCount; i++)
            _rtScratch[i] = _rtEvents[i];
        _rtEventCount = 0;
        ReleaseEvLock();

        for (int i = 0; i < evCount; i++)
            ApplyParamRt(_rtScratch[i]);
    }

    public void ApplyParamRt(in PluginParamChangeEvent ev)
    {
        float plain = NormalizedToPlainLive(ev.ParamId, ev.NormValue);
        switch ((DawParamId)ev.ParamId)
        {
            case DawParamId.InputGainDb:
                _processor.Chain.SetGlobalParamsRt(inputGainDb: plain);
                break;
            case DawParamId.OutputGainDb:
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
                _processor.Chain.SwitchSceneRt(Math.Clamp((int)MathF.Round(plain), 0, 7));
                RefreshSlotParamsFromEngineRt();
                RefreshAmpMacroParamsFromEngineRt();
                WriteNorm((uint)DawParamId.RoutingMode, _processor.Chain.RoutingMode != ChainRoutingMode.Serial ? 1f : 0f);
                WriteNorm((uint)DawParamId.ParallelMix, Math.Clamp(_processor.Chain.ParallelMix, 0f, 1f));
                break;
            case DawParamId.TempoBpm:
                _processor.Chain.SetTempoBpmRt(plain);
                break;
            case DawParamId.RoutingMode:
                _processor.Chain.SetGlobalParamsRt(routing: plain >= 0.5f ? ChainRoutingMode.ParallelSplit : ChainRoutingMode.Serial);
                break;
            case DawParamId.ParallelMix:
                _processor.Chain.SetGlobalParamsRt(parallelMix: plain / 100f);
                break;
            case DawParamId.AmpGain:
                SetPrimaryAmpMacroRt(0, plain);
                break;
            case DawParamId.AmpBass:
                SetPrimaryAmpMacroRt(1, plain);
                break;
            case DawParamId.AmpMid:
                SetPrimaryAmpMacroRt(2, plain);
                break;
            case DawParamId.AmpTreble:
                SetPrimaryAmpMacroRt(3, plain);
                break;
            case DawParamId.AmpMaster:
                SetPrimaryAmpMacroRt(4, plain);
                break;
            case DawParamId.AmpSag:
                SetPrimaryAmpMacroRt(5, plain);
                break;
            case DawParamId.AmpPresence:
                SetPrimaryAmpMacroRt(6, plain);
                break;
            default:
                if (DawParameterCatalog.TryDecodeSlotParam(ev.ParamId, out int slotIdx, out bool isBypass, out int knobIdx))
                {
                    if (isBypass)
                    {
                        _processor.Chain.SetSlotBypassRt(slotIdx, plain >= 0.5f);
                    }
                    else
                    {
                        _processor.Chain.SetSlotParamRt(slotIdx, knobIdx, plain);
                        if (slotIdx == Volatile.Read(ref _primaryAmpSlot) && knobIdx >= 0 && knobIdx < 7)
                        {
                            uint ampMacroId = (uint)((int)DawParamId.AmpGain + knobIdx);
                            WriteNorm(ampMacroId, DawParameterCatalog.PlainToNormalized(ampMacroId, plain));
                            MarkDawDirty((int)ampMacroId);
                        }
                    }
                }
                break;
        }
    }

    private void SetPrimaryAmpMacroRt(int macroIdx, float plain)
    {
        _processor.Chain.SetPrimaryAmpParamRt(macroIdx, plain);
        int ampSlot = Volatile.Read(ref _primaryAmpSlot);
        if (ampSlot >= 0)
        {
            uint slotParamId = DawParameterCatalog.SlotKnobParamId(ampSlot, macroIdx);
            WriteNorm(slotParamId, PlainToNormalizedLive(slotParamId, plain));
            MarkDawDirty((int)slotParamId);
        }
    }

    public void CopyRecentSamples(Span<float> dst) => CopyRingTail(_wetRing, ref _wetRingPos, dst);

    public void CopyRecentDrySamples(Span<float> dst) => CopyRingTail(_dryRing, ref _dryRingPos, dst);

    private static void CopyRingTail(float[] ring, ref int posRef, Span<float> dst)
    {
        if (dst.IsEmpty) return;
        int count = Math.Min(dst.Length, TelemetryRingCap);
        int head = Volatile.Read(ref posRef);
        int start = (head - count) & TelemetryRingMask;
        for (int i = 0; i < count; i++)
            dst[i] = ring[(start + i) & TelemetryRingMask];
        if (count < dst.Length)
            dst.Slice(count).Clear();
    }

    public void ReadAndResetMeters(out float peakL, out float peakR, out bool clip)
    {
        peakL = Volatile.Read(ref _peakL);
        peakR = Volatile.Read(ref _peakR);
        clip = Interlocked.Exchange(ref _clipFlag, 0) == 1;
        Volatile.Write(ref _peakL, 0f);
        Volatile.Write(ref _peakR, 0f);
    }

    public void ReadAndResetIoMeters(
        out float inPeakL,
        out float inPeakR,
        out bool inClip,
        out float outPeakL,
        out float outPeakR,
        out bool outClip)
    {
        _processor.TakeRtMeters(out inPeakL, out inPeakR, out inClip, out bool toneOutClip);
        ReadAndResetMeters(out outPeakL, out outPeakR, out bool postClip);
        outClip = postClip || toneOutClip;
    }

    public void SetIoGainFromUi(float? inputGainDb, float? outputGainDb)
    {
        _processor.Chain.SetIoGainFromUi(inputGainDb, outputGainDb);
        if (inputGainDb.HasValue)
            WriteNorm((uint)DawParamId.InputGainDb, DawParameterCatalog.PlainToNormalized((uint)DawParamId.InputGainDb, inputGainDb.Value));
        if (outputGainDb.HasValue)
            WriteNorm((uint)DawParamId.OutputGainDb, DawParameterCatalog.PlainToNormalized((uint)DawParamId.OutputGainDb, outputGainDb.Value));
    }

    public byte[] SaveStateBytes(
        List<SignalChainConfigDto>? savedPresets = null,
        List<GuitarProfileDto>? guitarProfiles = null,
        string? activeGuitarProfileId = null)
    {
        var liveChain = _processor.Chain.ExportLiveStateSnapshot();
        SyncParamsFromSnapshot(liveChain);

        var normCopy = new float[DawParameterCatalog.TotalParameterCount];
        for (int i = 0; i < normCopy.Length; i++)
            normCopy[i] = GetParamNormalized((uint)i);

        var env = new PluginStateEnvelope
        {
            FormatVersion = 3,
            SelectedToneId = _processor.ActiveProfile?.Id,
            ToneMode = (int)_processor.Mode,
            CustomCabIrPath = _processor.CustomCabIrPath,
            DriveMultiplier = _processor.DriveMultiplier,
            WetMix = _processor.WetMix,
            OutputGainDb = _processor.OutputGainDb,
            GateThresholdDb = _processor.GateThresholdDb,
            HostBpmSync = HostBpmSync,
            TunerMute = TunerMute,
            WhiteBox = _processor.WhiteBox.GetParamsSnapshot(),
            SignalChain = liveChain,
            ParamsNorm = normCopy,
            SavedPresets = savedPresets,
            GuitarProfiles = guitarProfiles,
            ActiveGuitarProfileId = activeGuitarProfileId,
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(env, PluginJsonContext.Default.PluginStateEnvelope);
        byte[] packet = new byte[StateMagic.Length + json.Length];
        StateMagic.CopyTo(packet, 0);
        json.CopyTo(packet, StateMagic.Length);
        return packet;
    }

    public bool LoadStateBytes(ReadOnlySpan<byte> bytes)
        => TryLoadStateEnvelope(bytes, out _);

    public bool TryLoadStateEnvelope(ReadOnlySpan<byte> bytes, out PluginStateEnvelope? loadedEnvelope)
    {
        loadedEnvelope = null;
        if (bytes.Length <= StateMagic.Length || !bytes[..StateMagic.Length].SequenceEqual(StateMagic))
            return false;

        try
        {
            var env = JsonSerializer.Deserialize(bytes[StateMagic.Length..], PluginJsonContext.Default.PluginStateEnvelope);
            if (env == null) return false;

            var models = ToneProfileStore.LoadAll();
            _processor.SetModelLibrary(models);
            if (!string.IsNullOrEmpty(env.SelectedToneId))
                _processor.ActiveProfile = models.FirstOrDefault(m => m.Id == env.SelectedToneId) ?? _processor.ResolveModel(env.SelectedToneId);

            if (!string.IsNullOrEmpty(env.CustomCabIrPath) && File.Exists(env.CustomCabIrPath))
            {
                try { _processor.LoadCustomCabIr(env.CustomCabIrPath); } catch { }
            }

            _processor.Mode = (ToneMode)Math.Clamp(env.ToneMode, 0, 6);
            _processor.DriveMultiplier = Math.Clamp(env.DriveMultiplier, 0.2f, 2.2f);
            _processor.WetMix = Math.Clamp(env.WetMix, 0f, 1f);
            _processor.OutputGainDb = Math.Clamp(env.OutputGainDb, -24f, 24f);
            _processor.GateThresholdDb = Math.Clamp(env.GateThresholdDb, -80f, -20f);
            HostBpmSync = env.HostBpmSync;
            TunerMute = env.TunerMute;

            if (env.WhiteBox != null)
                _processor.WhiteBox.UpdateParams(env.WhiteBox);

            if (env.SignalChain != null)
            {
                LoadChainConfig(env.SignalChain);
                for (int i = 0; i < DawParameterCatalog.TotalParameterCount; i++)
                    MarkDawDirty(i);
            }
            else if (env.ParamsNorm is { Length: > 0 })
            {
                int count = Math.Min(env.ParamsNorm.Length, DawParameterCatalog.TotalParameterCount);
                for (int i = 0; i < count; i++)
                {
                    if (i == (int)DawParamId.ActiveScene) continue;
                    float clamped = Math.Clamp(env.ParamsNorm[i], 0f, 1f);
                    Volatile.Write(ref _paramNorm[i], clamped);
                    ApplyParamRt(new((uint)i, clamped, 0));
                    MarkDawDirty(i);
                }
            }

            loadedEnvelope = env;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Deactivate();
        _recorder.Dispose();
    }
}
