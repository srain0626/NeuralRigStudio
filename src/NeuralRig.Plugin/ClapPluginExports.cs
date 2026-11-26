using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NeuralRigStudio.Audio;

namespace NeuralRigStudio.Plugin;

/// <summary>
/// CLAP v1.2 네이티브 C ABI 구조체 정의 및 NativeAOT [UnmanagedCallersOnly] 엔트리 (Milestone 5 / v2.0).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ClapVersion
{
    public uint Major;
    public uint Minor;
    public uint Revision;

    public static ClapVersion Current => new() { Major = 1, Minor = 2, Revision = 2 };
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ClapPluginEntryNative
{
    public ClapVersion ClapVersion;
    public delegate* unmanaged[Cdecl]<byte*, byte> Init;
    public delegate* unmanaged[Cdecl]<void> Deinit;
    public delegate* unmanaged[Cdecl]<byte*, nint> GetFactory;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ClapPluginFactoryNative
{
    public delegate* unmanaged[Cdecl]<nint, uint> GetPluginCount;
    public delegate* unmanaged[Cdecl]<nint, uint, nint> GetPluginDescriptor;
    public delegate* unmanaged[Cdecl]<nint, nint, byte*, nint> CreatePlugin;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ClapPluginDescriptorNative
{
    public ClapVersion ClapVersion;
    public byte* Id;
    public byte* Name;
    public byte* Vendor;
    public byte* Url;
    public byte* ManualUrl;
    public byte* SupportUrl;
    public byte* Version;
    public byte* Description;
    public byte** Features;
}

[StructLayout(LayoutKind.Sequential)]
public struct ClapEventHeader
{
    public uint Size;
    public uint Time;
    public ushort SpaceId;
    public ushort Type;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
public struct ClapEventParamValue
{
    public ClapEventHeader Header;
    public uint ParamId;
    public nint Cookie;
    public int NoteId;
    public int PortIndex;
    public int Channel;
    public int Key;
    public double Value;
}

[StructLayout(LayoutKind.Sequential)]
public struct ClapEventTransport
{
    public ClapEventHeader Header;
    public uint Flags;
    public long SongPosBeats;
    public long SongPosSeconds;
    public double Tempo;
    public double TempoInc;
    public long LoopStartBeats;
    public long LoopEndBeats;
    public long LoopStartSeconds;
    public long LoopEndSeconds;
    public long BarStart;
    public int BarNumber;
    public ushort TsigNum;
    public ushort TsigDenom;
}

public static class ClapNativeMetadata
{
    public const string PluginId = "com.neuralrig.studio.vstclap";
    public const string PluginName = "NeuralRig Studio";
    public const string Vendor = "NeuralRig Audio";
    public const string Version = "3.2.0";
    public const string Description = "AVX2/FMA Neural Amp Modeler (WaveNet/LSTM/ConvNet) + 4x WhiteBox 2x8 Multi-FX";
    public static readonly string[] Features = { "audio-effect", "guitar", "amp-simulator", "multi-effects", "stereo" };
}

/// <summary>
/// CLAP v1.2 오디오 플러그인 호스트 인스턴스 래퍼 (RT 무할당 Process, 파라미터, Transport 동기화, State 저장/복원).
/// </summary>
public sealed class ClapPluginInstance : IDisposable
{
    private readonly NeuralRigPluginKernel _kernel = new();

    public string Id => ClapNativeMetadata.PluginId;
    public string Name => ClapNativeMetadata.PluginName;
    public string Version => ClapNativeMetadata.Version;
    public bool IsProcessing { get; private set; }
    public NeuralRigPluginKernel Kernel => _kernel;

    public bool Activate(double sampleRate, int minFrames, int maxFrames)
    {
        _kernel.Activate((int)Math.Round(sampleRate), maxFrames);
        return true;
    }

    public bool StartProcessing()
    {
        IsProcessing = true;
        return true;
    }

    public void StopProcessing() => IsProcessing = false;

    public void Deactivate()
    {
        IsProcessing = false;
        _kernel.Deactivate();
    }

    public int GetParameterCount() => DawParameterCatalog.ParameterCount;

    public DawParamDescriptor GetParameterInfo(int index) => DawParameterCatalog.All[index];

    public bool TryGetParameterValue(uint paramId, out double plainValue)
    {
        if (paramId >= DawParameterCatalog.ParameterCount)
        {
            plainValue = 0.0;
            return false;
        }
        float norm = _kernel.GetParamNormalized(paramId);
        plainValue = DawParameterCatalog.NormalizedToPlain(paramId, norm);
        return true;
    }

    public void EnqueueParamValueEvent(in ClapEventParamValue ev)
    {
        float norm = DawParameterCatalog.PlainToNormalized(ev.ParamId, (float)ev.Value);
        _kernel.EnqueueParamChange(ev.ParamId, norm, (int)ev.Header.Time);
    }

    /// <summary>CLAP RT 오디오 프로세스 (힙 할당 0 B).</summary>
    public int Process(
        Span<float> inLeft,
        Span<float> inRight,
        Span<float> outLeft,
        Span<float> outRight,
        int framesCount,
        double hostTempoBpm = 0.0,
        ReadOnlySpan<ClapEventParamValue> paramEvents = default)
    {
        if (framesCount <= 0) return 0; // CLAP_PROCESS_SLEEP / CONTINUE
        int n = Math.Min(framesCount, Math.Min(inLeft.Length, outLeft.Length));

        for (int i = 0; i < paramEvents.Length; i++)
        {
            ref readonly var ev = ref paramEvents[i];
            float norm = DawParameterCatalog.PlainToNormalized(ev.ParamId, (float)ev.Value);
            _kernel.EnqueueParamChange(ev.ParamId, norm, (int)ev.Header.Time);
        }

        inLeft.Slice(0, n).CopyTo(outLeft);
        inRight.Slice(0, n).CopyTo(outRight);
        _kernel.ProcessRt(outLeft, outRight, n, hostTempoBpm);
        return 1; // CLAP_PROCESS_CONTINUE
    }

    public byte[] SaveState() => _kernel.SaveStateBytes();
    public bool LoadState(ReadOnlySpan<byte> bytes) => _kernel.LoadStateBytes(bytes);

    public void Dispose() => _kernel.Dispose();
}

/// <summary>
/// NativeAOT 공유 라이브러리(.clap) 네이티브 심볼 export 테이블 (`clap_entry`).
/// </summary>
public static unsafe class ClapNativeEntry
{
    private static nint _utf8Id;
    private static nint _utf8Name;
    private static nint _utf8Vendor;
    private static nint _utf8Url;
    private static nint _utf8Version;
    private static nint _utf8Desc;
    private static nint _featuresArrayPtr;
    private static nint _descriptorPtr;
    private static nint _factoryPtr;
    private static nint _entryPtr;
    private static int _refCount;

    [UnmanagedCallersOnly(EntryPoint = "clap_entry")]
    public static nint GetClapEntry() => EnsureInitialized();

    public static nint EnsureInitialized()
    {
        if (_entryPtr != 0) return _entryPtr;

        _utf8Id = Marshal.StringToCoTaskMemUTF8(ClapNativeMetadata.PluginId);
        _utf8Name = Marshal.StringToCoTaskMemUTF8(ClapNativeMetadata.PluginName);
        _utf8Vendor = Marshal.StringToCoTaskMemUTF8(ClapNativeMetadata.Vendor);
        _utf8Url = Marshal.StringToCoTaskMemUTF8("https://github.com/neuralrig/studio");
        _utf8Version = Marshal.StringToCoTaskMemUTF8(ClapNativeMetadata.Version);
        _utf8Desc = Marshal.StringToCoTaskMemUTF8(ClapNativeMetadata.Description);

        int fCount = ClapNativeMetadata.Features.Length;
        byte** fPtrs = (byte**)NativeMemory.AllocZeroed((nuint)(fCount + 1), (nuint)sizeof(byte*));
        for (int i = 0; i < fCount; i++)
            fPtrs[i] = (byte*)Marshal.StringToCoTaskMemUTF8(ClapNativeMetadata.Features[i]);
        fPtrs[fCount] = null;
        _featuresArrayPtr = (nint)fPtrs;

        var desc = (ClapPluginDescriptorNative*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ClapPluginDescriptorNative));
        desc->ClapVersion = ClapVersion.Current;
        desc->Id = (byte*)_utf8Id;
        desc->Name = (byte*)_utf8Name;
        desc->Vendor = (byte*)_utf8Vendor;
        desc->Url = (byte*)_utf8Url;
        desc->ManualUrl = (byte*)_utf8Url;
        desc->SupportUrl = (byte*)_utf8Url;
        desc->Version = (byte*)_utf8Version;
        desc->Description = (byte*)_utf8Desc;
        desc->Features = fPtrs;
        _descriptorPtr = (nint)desc;

        var factory = (ClapPluginFactoryNative*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ClapPluginFactoryNative));
        factory->GetPluginCount = &FactoryGetPluginCount;
        factory->GetPluginDescriptor = &FactoryGetPluginDescriptor;
        factory->CreatePlugin = &FactoryCreatePlugin;
        _factoryPtr = (nint)factory;

        var entry = (ClapPluginEntryNative*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ClapPluginEntryNative));
        entry->ClapVersion = ClapVersion.Current;
        entry->Init = &EntryInit;
        entry->Deinit = &EntryDeinit;
        entry->GetFactory = &EntryGetFactory;
        _entryPtr = (nint)entry;
        return _entryPtr;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte EntryInit(byte* pluginPath)
    {
        Interlocked.Increment(ref _refCount);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void EntryDeinit()
    {
        Interlocked.Decrement(ref _refCount);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint EntryGetFactory(byte* factoryId)
    {
        string? req = Marshal.PtrToStringUTF8((nint)factoryId);
        return string.Equals(req, "clap.plugin-factory", StringComparison.Ordinal) ? _factoryPtr : 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint FactoryGetPluginCount(nint factory) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint FactoryGetPluginDescriptor(nint factory, uint index) => index == 0 ? _descriptorPtr : 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint FactoryCreatePlugin(nint factory, nint hostPtr, byte* pluginId)
    {
        string? req = Marshal.PtrToStringUTF8((nint)pluginId);
        if (!string.Equals(req, ClapNativeMetadata.PluginId, StringComparison.Ordinal))
            return 0;
        var inst = new ClapPluginInstance();
        return GCHandle.ToIntPtr(GCHandle.Alloc(inst));
    }
}
