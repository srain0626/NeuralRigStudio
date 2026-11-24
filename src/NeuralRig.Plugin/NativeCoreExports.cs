using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace NeuralRigStudio.Plugin;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NrsParamInfoNative
{
    public uint Id;
    public int StepCount;
    public float MinValue;
    public float MaxValue;
    public float DefaultNormalized;
    public byte Flags; // bit 0 = CanAutomate
    public fixed byte NameUtf8[128];
    public fixed byte ShortNameUtf8[64];
    public fixed byte UnitsUtf8[32];
}

internal sealed unsafe class NativePluginSession : IDisposable
{
    private readonly nint _userData;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int, void> _webSendCb;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, int, double, void> _hostParamCb;
    private int _disposed;

    public NeuralRigPluginKernel Kernel { get; }
    public PluginWebBridge Bridge { get; }

    public NativePluginSession(
        nint userData,
        delegate* unmanaged[Cdecl]<nint, byte*, int, void> webSendCb,
        delegate* unmanaged[Cdecl]<nint, uint, int, double, void> hostParamCb)
    {
        _userData = userData;
        _webSendCb = webSendCb;
        _hostParamCb = hostParamCb;
        Kernel = new NeuralRigPluginKernel();
        Bridge = new PluginWebBridge(Kernel, SendJsonToNative, EmitHostParamGesture);
    }

    private void SendJsonToNative(string json)
    {
        if (Volatile.Read(ref _disposed) == 1 || _webSendCb == null)
            return;

        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        fixed (byte* ptr = utf8)
        {
            _webSendCb(_userData, ptr, utf8.Length);
        }
    }

    private void EmitHostParamGesture(uint paramId, int phase, double norm01)
    {
        if (Volatile.Read(ref _disposed) == 1 || _hostParamCb == null)
            return;
        _hostParamCb(_userData, paramId, phase, Math.Clamp(norm01, 0.0, 1.0));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        Bridge.Dispose();
        Kernel.Dispose();
    }
}

/// <summary>
/// C++20 VST3 COM 호스트 셸과 결합되는 NativeAOT C-ABI 공유 라이브러리 엔트리 포인트 (`NeuralRig.NativeCore.dll`).
/// </summary>
public static unsafe class NativeCoreExports
{
    private const int AbiVersion = 1;

    private static NativePluginSession? Unwrap(nint handle)
    {
        if (handle == 0) return null;
        return GCHandle.FromIntPtr(handle).Target as NativePluginSession;
    }

    private static void WriteFixedUtf8(string text, byte* dst, int maxBytes)
    {
        if (dst == null || maxBytes <= 0) return;
        if (string.IsNullOrEmpty(text))
        {
            dst[0] = 0;
            return;
        }
        int written = Encoding.UTF8.GetBytes(text.AsSpan(), new Span<byte>(dst, maxBytes - 1));
        dst[written] = 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_abi_version", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int NrsAbiVersion() => AbiVersion;

    [UnmanagedCallersOnly(EntryPoint = "nrs_create", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint NrsCreate(
        delegate* unmanaged[Cdecl]<nint, byte*, int, void> webSendCb,
        delegate* unmanaged[Cdecl]<nint, uint, int, double, void> hostParamCb,
        nint userData)
    {
        try
        {
            var session = new NativePluginSession(userData, webSendCb, hostParamCb);
            return GCHandle.ToIntPtr(GCHandle.Alloc(session));
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_destroy", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsDestroy(nint handle)
    {
        if (handle == 0) return;
        try
        {
            var gch = GCHandle.FromIntPtr(handle);
            if (gch.Target is NativePluginSession session)
                session.Dispose();
            gch.Free();
        }
        catch { }
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_set_parent_hwnd", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsSetParentHwnd(nint handle, nint hwnd)
        => Unwrap(handle)?.Bridge.SetParentHwnd(hwnd);

    [UnmanagedCallersOnly(EntryPoint = "nrs_activate", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsActivate(nint handle, int sampleRate, int maxBlockSize)
        => Unwrap(handle)?.Kernel.Activate(sampleRate, maxBlockSize);

    [UnmanagedCallersOnly(EntryPoint = "nrs_deactivate", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsDeactivate(nint handle)
        => Unwrap(handle)?.Kernel.Deactivate();

    [UnmanagedCallersOnly(EntryPoint = "nrs_process", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsProcess(
        nint handle,
        float* inL,
        float* inR,
        float* outL,
        float* outR,
        int numSamples,
        double hostBpm,
        byte hasHostBpm)
    {
        var session = Unwrap(handle);
        if (session == null) return;

        if (numSamples <= 0 || outL == null || outR == null)
        {
            session.Kernel.FlushPendingParamsNoAudio();
            return;
        }

        var sl = new Span<float>(outL, numSamples);
        var sr = new Span<float>(outR, numSamples);
        if (inL != null && inL != outL)
            new ReadOnlySpan<float>(inL, numSamples).CopyTo(sl);
        else if (inL == null)
            sl.Clear();

        if (inR != null && inR != outR)
            new ReadOnlySpan<float>(inR, numSamples).CopyTo(sr);
        else if (inR == null)
            sl.CopyTo(sr);

        session.Kernel.ProcessRt(sl, sr, numSamples, hasHostBpm != 0 ? hostBpm : 0.0);
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_get_param_count", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int NrsGetParamCount() => DawParameterCatalog.TotalParameterCount;

    [UnmanagedCallersOnly(EntryPoint = "nrs_get_param_info", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int NrsGetParamInfo(nint handle, uint paramId, NrsParamInfoNative* outInfo)
    {
        if (outInfo == null || paramId >= DawParameterCatalog.TotalParameterCount)
            return 0;

        var session = Unwrap(handle);
        DawParamDescriptor desc = session != null
            ? session.Kernel.GetParamDisplayInfo(paramId, out _, out _)
            : DawParameterCatalog.All[paramId];

        outInfo->Id = desc.Id;
        outInfo->StepCount = desc.StepCount;
        outInfo->MinValue = desc.MinValue;
        outInfo->MaxValue = desc.MaxValue;
        outInfo->DefaultNormalized = DawParameterCatalog.PlainToNormalizedRange(desc.DefaultValue, desc.MinValue, desc.MaxValue);
        outInfo->Flags = 1; // kCanAutomate

        WriteFixedUtf8(desc.Name, outInfo->NameUtf8, 128);
        WriteFixedUtf8(desc.Name, outInfo->ShortNameUtf8, 64);
        WriteFixedUtf8(desc.Unit, outInfo->UnitsUtf8, 32);
        return 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_get_param_normalized", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static double NrsGetParamNormalized(nint handle, uint paramId)
        => Unwrap(handle)?.Kernel.GetParamNormalized(paramId) ?? 0.0;

    [UnmanagedCallersOnly(EntryPoint = "nrs_set_param_normalized", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsSetParamNormalized(nint handle, uint paramId, double norm01, int sampleOffset)
        => Unwrap(handle)?.Kernel.EnqueueParamChange(paramId, (float)norm01, sampleOffset);

    [UnmanagedCallersOnly(EntryPoint = "nrs_param_norm_to_plain", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static double NrsParamNormToPlain(nint handle, uint paramId, double norm01)
    {
        var session = Unwrap(handle);
        return session != null
            ? session.Kernel.NormalizedToPlainLive(paramId, (float)norm01)
            : DawParameterCatalog.NormalizedToPlain(paramId, (float)norm01);
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_param_plain_to_norm", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static double NrsParamPlainToNorm(nint handle, uint paramId, double plain)
    {
        var session = Unwrap(handle);
        return session != null
            ? session.Kernel.PlainToNormalizedLive(paramId, (float)plain)
            : DawParameterCatalog.PlainToNormalized(paramId, (float)plain);
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_format_param_value", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int NrsFormatParamValue(nint handle, uint paramId, double norm01, byte* utf8Out, int maxBytes)
    {
        if (utf8Out == null || maxBytes <= 0) return 0;
        var session = Unwrap(handle);
        string text = session != null
            ? session.Kernel.FormatParamValue(paramId, (float)norm01)
            : DawParameterCatalog.FormatNormalizedValue(paramId, (float)norm01);
        int written = Encoding.UTF8.GetBytes(text.AsSpan(), new Span<byte>(utf8Out, maxBytes - 1));
        utf8Out[written] = 0;
        return written;
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_save_state", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static byte* NrsSaveState(nint handle, int* outLen)
    {
        if (outLen != null) *outLen = 0;
        var session = Unwrap(handle);
        if (session == null) return null;

        try
        {
            byte[] bytes = session.Bridge.SavePluginStateBytes();
            if (bytes.Length == 0) return null;
            byte* buf = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
            bytes.AsSpan().CopyTo(new Span<byte>(buf, bytes.Length));
            if (outLen != null) *outLen = bytes.Length;
            return buf;
        }
        catch
        {
            return null;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_free_buffer", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsFreeBuffer(nint ptr)
    {
        if (ptr != 0)
            NativeMemory.Free((void*)ptr);
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_load_state", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int NrsLoadState(nint handle, byte* data, int len)
    {
        var session = Unwrap(handle);
        if (session == null || data == null || len <= 0) return 0;
        try
        {
            return session.Bridge.LoadPluginStateBytes(new ReadOnlySpan<byte>(data, len)) ? 1 : 0;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_web_post_message", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsWebPostMessage(nint handle, byte* utf8Json, int len)
    {
        var session = Unwrap(handle);
        if (session == null || utf8Json == null || len <= 0) return;
        try
        {
            string json = Encoding.UTF8.GetString(utf8Json, len);
            session.Bridge.HandleWebMessage(json);
        }
        catch { }
    }

    [UnmanagedCallersOnly(EntryPoint = "nrs_poll_ui", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void NrsPollUi(nint handle)
    {
        try
        {
            Unwrap(handle)?.Bridge.TickUi();
        }
        catch { }
    }
}
