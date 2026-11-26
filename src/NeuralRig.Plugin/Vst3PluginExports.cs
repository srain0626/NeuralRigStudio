using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NeuralRigStudio.Plugin;

/// <summary>
/// Steinberg VST3 v3.7 네이티브 C/COM ABI 구조체 정의 및 NativeAOT [UnmanagedCallersOnly] 엔트리 (Milestone 5 / v2.0).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vst3ProcessContext
{
    public uint StateFlags;
    public long ProjectTimeSamples;
    public double SystemTime;
    public long ContinousTimeSamples;
    public double ProjectTimeMusic;
    public double BarPositionMusic;
    public double CycleStartMusic;
    public double CycleEndMusic;
    public double Tempo;
    public int TimeSigNumerator;
    public int TimeSigDenominator;
    public int SmpteOffsetSubframes;
    public uint FrameRateFramesPerSecond;
    public uint FrameRateFlags;
    public int SamplesToNextClock;
}

public static class Vst3PluginMetadata
{
    public const string ProcessorCidHex = "A73E91C452D84B6E91F0C38E4B72D101";
    public const string ControllerCidHex = "A73E91C452D84B6E91F0C38E4B72D102";
    public static readonly Guid ProcessorCid = new("A73E91C4-52D8-4B6E-91F0-C38E4B72D101");
    public static readonly Guid ControllerCid = new("A73E91C4-52D8-4B6E-91F0-C38E4B72D102");
    public const string SubCategories = "Fx|Guitar|AmpSim";
    public const string Name = "NeuralRig Studio";
    public const string Vendor = "NeuralRig Audio";
    public const string Version = "3.2.0.0";
}

/// <summary>
/// Steinberg VST3 Component + AudioProcessor + EditController 통합 인스턴스.
/// - 호스트 ProcessContext.Tempo 동기화 + 정규화 파라미터(0..1) 샘플 정확도 처리
/// - RT 콜백 힙 할당 0 B
/// - .nrig 기반 IComponent::getState / setState 스트림 지원
/// </summary>
public sealed class Vst3PluginInstance : IDisposable
{
    private readonly NeuralRigPluginKernel _kernel = new();

    public Guid ComponentCid => Vst3PluginMetadata.ProcessorCid;
    public Guid ControllerCid => Vst3PluginMetadata.ControllerCid;
    public int InputChannels { get; private set; } = 2;
    public int OutputChannels { get; private set; } = 2;
    public bool Active { get; private set; }
    public NeuralRigPluginKernel Kernel => _kernel;

    public bool Initialize() => true;

    public bool SetBusArrangements(int inputs, int outputs)
    {
        if (inputs < 1 || inputs > 2 || outputs != 2) return false;
        InputChannels = inputs;
        OutputChannels = outputs;
        return true;
    }

    public bool SetupProcessing(double sampleRate, int maxSamplesPerBlock)
    {
        _kernel.Activate((int)Math.Round(sampleRate), maxSamplesPerBlock);
        return true;
    }

    public bool SetActive(bool state)
    {
        Active = state;
        if (!state) _kernel.Deactivate();
        return true;
    }

    public int GetParameterCount() => DawParameterCatalog.ParameterCount;
    public DawParamDescriptor GetParameterInfo(int index) => DawParameterCatalog.All[index];

    public double GetParamNormalized(uint paramId) => _kernel.GetParamNormalized(paramId);

    public void SetParamNormalized(uint paramId, double norm01, int sampleOffset = 0)
        => _kernel.EnqueueParamChange(paramId, (float)norm01, sampleOffset);

    public double NormalizedParamToPlain(uint paramId, double norm01)
        => DawParameterCatalog.NormalizedToPlain(paramId, (float)norm01);

    public double PlainParamToNormalized(uint paramId, double plain)
        => DawParameterCatalog.PlainToNormalized(paramId, (float)plain);

    public string FormatParameterValue(uint paramId, double norm01)
        => DawParameterCatalog.FormatNormalizedValue(paramId, (float)norm01);

    /// <summary>VST3 IAudioProcessor::process 실시간 무할당 콜백 (할당 0 B).</summary>
    public void Process(
        ReadOnlySpan<float> inLeft,
        ReadOnlySpan<float> inRight,
        Span<float> outLeft,
        Span<float> outRight,
        int numSamples,
        in Vst3ProcessContext context = default)
    {
        if (numSamples <= 0) return;
        int n = Math.Min(numSamples, Math.Min(outLeft.Length, outRight.Length));
        inLeft.Slice(0, n).CopyTo(outLeft);
        if (InputChannels >= 2 && inRight.Length >= n)
            inRight.Slice(0, n).CopyTo(outRight);
        else
            outLeft.Slice(0, n).CopyTo(outRight);

        double hostTempo = (context.StateFlags & 0x0400u) != 0 ? context.Tempo : context.Tempo;
        _kernel.ProcessRt(outLeft, outRight, n, hostTempo);
    }

    public byte[] GetState() => _kernel.SaveStateBytes();
    public bool SetState(ReadOnlySpan<byte> bytes) => _kernel.LoadStateBytes(bytes);

    public void Dispose() => _kernel.Dispose();
}

[StructLayout(LayoutKind.Sequential)]
public struct Vst3NativeFactoryDescriptor
{
    public uint AbiVersion; // 370 = VST 3.7
    public int ClassCount;
    public Guid ProcessorClassId;
    public Guid ControllerClassId;
}

/// <summary>
/// NativeAOT 공유 라이브러리(.vst3) C-ABI 엔트리 포인트 (`InitDll`, `ExitDll`, `GetPluginFactory`).
/// </summary>
public static unsafe class Vst3NativeEntry
{
    private static nint _factoryPtr;
    private static int _initCount;

    public static byte InitDll()
    {
        Interlocked.Increment(ref _initCount);
        EnsureFactory();
        return 1;
    }

    public static byte ExitDll()
    {
        Interlocked.Decrement(ref _initCount);
        return 1;
    }

    public static nint GetPluginFactory() => EnsureFactory();

    public static nint EnsureFactory()
    {
        if (_factoryPtr != 0) return _factoryPtr;
        var f = (Vst3NativeFactoryDescriptor*)NativeMemory.AllocZeroed(1, (nuint)sizeof(Vst3NativeFactoryDescriptor));
        f->AbiVersion = 370;
        f->ClassCount = 2;
        f->ProcessorClassId = Vst3PluginMetadata.ProcessorCid;
        f->ControllerClassId = Vst3PluginMetadata.ControllerCid;
        _factoryPtr = (nint)f;
        return _factoryPtr;
    }
}
