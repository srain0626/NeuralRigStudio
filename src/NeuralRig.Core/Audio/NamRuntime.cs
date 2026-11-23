using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 통합 공식 NAM 신경망 런타임 워크스페이스 (WaveNet / LSTM / ConvNet 모두 지원).
/// UI/비실시간 스레드에서 생성·사전 할당되며 오디오 콜백(RT)에서는 힙 할당 0 B로 동작한다.
/// </summary>
public sealed class NamWorkspace
{
    // ── WaveNet State ──
    private sealed class WaveNetLayerState
    {
        public readonly float[] Ring;
        public int WritePos;

        public WaveNetLayerState(CompiledWaveNet.Layer layer)
        {
            Ring = new float[layer.RingCapacity * layer.PadChannels];
        }

        public void Reset()
        {
            Array.Clear(Ring);
            WritePos = 0;
        }
    }

    private sealed class WaveNetArrayHeadState
    {
        public readonly float[]? HeadRing;
        public int WritePos;

        public WaveNetArrayHeadState(CompiledWaveNet.LayerArray arr)
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

    private sealed class WaveNetRuntimeState
    {
        public readonly CompiledWaveNet Net;
        public readonly WaveNetLayerState[][] LayerStates;
        public readonly WaveNetArrayHeadState[] HeadStates;

        public readonly float[] YBuf;
        public readonly float[] NextYBuf;
        public readonly float[] MidBuf;
        public readonly float[] PostBuf;
        public readonly float[] HeadAcc;
        public readonly float[] HeadBus;

        public WaveNetRuntimeState(CompiledWaveNet net)
        {
            Net = net;
            LayerStates = new WaveNetLayerState[net.Arrays.Length][];
            HeadStates = new WaveNetArrayHeadState[net.Arrays.Length];
            for (int bi = 0; bi < net.Arrays.Length; bi++)
            {
                var arr = net.Arrays[bi];
                LayerStates[bi] = new WaveNetLayerState[arr.Layers.Length];
                for (int li = 0; li < arr.Layers.Length; li++)
                    LayerStates[bi][li] = new WaveNetLayerState(arr.Layers[li]);
                HeadStates[bi] = new WaveNetArrayHeadState(arr);
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

    // ── LSTM State ──
    private sealed class LstmLayerState
    {
        public readonly float[] H;
        public readonly float[] C;
        public readonly float[] XH;
        public readonly float[] Ifgo;
        private readonly float[] _initH;
        private readonly float[] _initC;

        public LstmLayerState(CompiledLstm.Layer layer)
        {
            H = new float[layer.HiddenSize];
            C = new float[layer.HiddenSize];
            XH = new float[layer.InputSize + layer.HiddenSize];
            Ifgo = new float[layer.Pad4H];
            _initH = layer.InitH;
            _initC = layer.InitC;
            Reset();
        }

        public void Reset()
        {
            Array.Copy(_initH, H, H.Length);
            Array.Copy(_initC, C, C.Length);
            Array.Clear(XH);
            Array.Clear(Ifgo);
        }
    }

    private sealed class LstmRuntimeState
    {
        public readonly CompiledLstm Model;
        public readonly LstmLayerState[] Layers;
        public readonly float[] SignalBuf;

        public LstmRuntimeState(CompiledLstm model)
        {
            Model = model;
            Layers = new LstmLayerState[model.Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
                Layers[i] = new LstmLayerState(model.Layers[i]);
            SignalBuf = new float[Math.Max(16, model.MaxStateSize)];
        }

        public void Reset()
        {
            for (int i = 0; i < Layers.Length; i++)
                Layers[i].Reset();
            Array.Clear(SignalBuf);
        }
    }

    // ── ConvNet State ──
    private sealed class ConvNetLayerState
    {
        public readonly float[] Ring;
        public int WritePos;

        public ConvNetLayerState(CompiledConvNet.Layer layer)
        {
            Ring = new float[layer.RingCapacity * layer.PadInChannels];
        }

        public void Reset()
        {
            Array.Clear(Ring);
            WritePos = 0;
        }
    }

    private sealed class ConvNetRuntimeState
    {
        public readonly CompiledConvNet Model;
        public readonly ConvNetLayerState[] Layers;
        public readonly float[] YBuf;
        public readonly float[] NextYBuf;

        public ConvNetRuntimeState(CompiledConvNet model)
        {
            Model = model;
            Layers = new ConvNetLayerState[model.Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
                Layers[i] = new ConvNetLayerState(model.Layers[i]);
            int cap = Math.Max(16, model.MaxChannelsPadded);
            YBuf = new float[cap];
            NextYBuf = new float[cap];
        }

        public void Reset()
        {
            for (int i = 0; i < Layers.Length; i++)
                Layers[i].Reset();
            Array.Clear(YBuf);
            Array.Clear(NextYBuf);
        }
    }

    public ToneProfile Profile { get; }

    private readonly WaveNetRuntimeState? _waveNet;
    private readonly LstmRuntimeState? _lstm;
    private readonly ConvNetRuntimeState? _convNet;

    public bool IsReady => _waveNet != null || _lstm != null || _convNet != null;

    public NamWorkspace(ToneProfile profile)
    {
        Profile = profile;
        if (profile.CompiledNet != null)
            _waveNet = new WaveNetRuntimeState(profile.CompiledNet);
        else if (profile.CompiledLstm != null)
            _lstm = new LstmRuntimeState(profile.CompiledLstm);
        else if (profile.CompiledConvNet != null)
            _convNet = new ConvNetRuntimeState(profile.CompiledConvNet);
    }

    public void Reset()
    {
        _waveNet?.Reset();
        _lstm?.Reset();
        _convNet?.Reset();
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        int n = Math.Min(input.Length, output.Length);
        if (n <= 0) return;

        if (_waveNet != null)
        {
            RunWaveNet(_waveNet, input.Slice(0, n), output.Slice(0, n));
            return;
        }
        if (_lstm != null)
        {
            RunLstm(_lstm, input.Slice(0, n), output.Slice(0, n));
            return;
        }
        if (_convNet != null)
        {
            RunConvNet(_convNet, input.Slice(0, n), output.Slice(0, n));
            return;
        }
    }

    // =========================================================================
    // AVX2 / FMA 공식 NAM Multi-Block Gated Dilated WaveNet 실시간 추론 커널
    // =========================================================================
    private static void RunWaveNet(WaveNetRuntimeState ws, ReadOnlySpan<float> input, Span<float> output)
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

                ref float rechW = ref MemoryMarshal.GetArrayDataReference(arr.RechannelW);
                ScaleRow(ref nextYRef, ref rechW, yRef, padCh);
                for (int ic = 1; ic < inCh; ic++)
                {
                    float v = Unsafe.Add(ref yRef, ic);
                    MacRow(ref nextYRef, ref Unsafe.Add(ref rechW, ic * padCh), v, padCh);
                }
                CopyVec(ref yRef, ref nextYRef, padCh);

                int headInPad = Pad8(arr.HeadInChannels);
                if (bi == 0)
                    ZeroVec(ref headAccRef, headInPad);
                else
                    CopyVec(ref headAccRef, ref headBusRef, headInPad);

                var layers = arr.Layers;
                var lStates = ws.LayerStates[bi];
                for (int li = 0; li < layers.Length; li++)
                {
                    var layer = layers[li];
                    var st = lStates[li];
                    int wPos = st.WritePos;
                    int ringMask = layer.RingMask;
                    ref float ringBase = ref MemoryMarshal.GetArrayDataReference(st.Ring);

                    CopyVec(ref Unsafe.Add(ref ringBase, wPos * padCh), ref yRef, padCh);

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

                    int bn = layer.Bottleneck;
                    if (layer.Gated)
                        ActivateGated(ref midRef, ref postRef, bn, layer.Act);
                    else
                        ActivatePlain(ref midRef, ref postRef, bn, layer.Act);

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

    // =========================================================================
    // AVX2 / FMA 공식 NAM Multi-Layer LSTM 실시간 추론 커널
    // =========================================================================
    private static void RunLstm(LstmRuntimeState ws, ReadOnlySpan<float> input, Span<float> output)
    {
        var model = ws.Model;
        var layers = model.Layers;
        var lStates = ws.Layers;
        int nLayers = layers.Length;
        int frames = input.Length;
        float[] headW = model.HeadW;
        float headB = model.HeadB;
        ref float sigRef = ref MemoryMarshal.GetArrayDataReference(ws.SignalBuf);
        ref float headWRef = ref MemoryMarshal.GetArrayDataReference(headW);

        for (int t = 0; t < frames; t++)
        {
            sigRef = input[t];

            for (int li = 0; li < nLayers; li++)
            {
                var layer = layers[li];
                var st = lStates[li];
                int inDim = layer.InputSize;
                int hDim = layer.HiddenSize;
                int totalIn = inDim + hDim;
                int pad4H = layer.Pad4H;

                ref float xhRef = ref MemoryMarshal.GetArrayDataReference(st.XH);
                ref float hRef = ref MemoryMarshal.GetArrayDataReference(st.H);
                ref float cRef = ref MemoryMarshal.GetArrayDataReference(st.C);
                ref float ifgoRef = ref MemoryMarshal.GetArrayDataReference(st.Ifgo);
                ref float wRef = ref MemoryMarshal.GetArrayDataReference(layer.W);
                ref float bRef = ref MemoryMarshal.GetArrayDataReference(layer.B);

                // xh = [input_or_prev_h, h_{t-1}]
                for (int k = 0; k < inDim; k++)
                    Unsafe.Add(ref xhRef, k) = Unsafe.Add(ref sigRef, k);
                for (int k = 0; k < hDim; k++)
                    Unsafe.Add(ref xhRef, inDim + k) = Unsafe.Add(ref hRef, k);

                // ifgo = B + sum_k xh[k] * W[k, :]
                CopyVec(ref ifgoRef, ref bRef, pad4H);
                for (int k = 0; k < totalIn; k++)
                {
                    float xv = Unsafe.Add(ref xhRef, k);
                    MacRow(ref ifgoRef, ref Unsafe.Add(ref wRef, k * pad4H), xv, pad4H);
                }

                // Gates [i, f, g, o], update C = f * C + i * g, H = o * tanh(C)
                int fOff = hDim;
                int gOff = hDim * 2;
                int oOff = hDim * 3;

                int j = 0;
                if (Vector256.IsHardwareAccelerated && hDim >= 8)
                {
                    for (; j <= hDim - 8; j += 8)
                    {
                        var vi = FastSigmoid256(Vector256.LoadUnsafe(ref ifgoRef, (nuint)j));
                        var vf = FastSigmoid256(Vector256.LoadUnsafe(ref ifgoRef, (nuint)(fOff + j)));
                        var vg = FastTanh256(Vector256.LoadUnsafe(ref ifgoRef, (nuint)(gOff + j)));
                        var vo = FastSigmoid256(Vector256.LoadUnsafe(ref ifgoRef, (nuint)(oOff + j)));
                        var vcPrev = Vector256.LoadUnsafe(ref cRef, (nuint)j);
                        var vcNew = vf * vcPrev + vi * vg;
                        var vhNew = vo * FastTanh256(vcNew);
                        Vector256.StoreUnsafe(vcNew, ref cRef, (nuint)j);
                        Vector256.StoreUnsafe(vhNew, ref hRef, (nuint)j);
                    }
                }
                for (; j < hDim; j++)
                {
                    float ig = FastSigmoidScalar(Unsafe.Add(ref ifgoRef, j));
                    float fg = FastSigmoidScalar(Unsafe.Add(ref ifgoRef, fOff + j));
                    float gg = FastTanhScalar(Unsafe.Add(ref ifgoRef, gOff + j));
                    float og = FastSigmoidScalar(Unsafe.Add(ref ifgoRef, oOff + j));
                    float cNew = fg * Unsafe.Add(ref cRef, j) + ig * gg;
                    float hNew = og * FastTanhScalar(cNew);
                    Unsafe.Add(ref cRef, j) = cNew;
                    Unsafe.Add(ref hRef, j) = hNew;
                }

                // Feed H to next layer's input buffer
                for (int k = 0; k < hDim; k++)
                    Unsafe.Add(ref sigRef, k) = Unsafe.Add(ref hRef, k);
            }

            // Linear head: headW · h_last + headB
            int lastH = layers[nLayers - 1].HiddenSize;
            float acc = headB;
            int idx = 0;
            if (Vector256.IsHardwareAccelerated && lastH >= 8)
            {
                var vsum = Vector256<float>.Zero;
                for (; idx <= lastH - 8; idx += 8)
                {
                    var vw = Vector256.LoadUnsafe(ref headWRef, (nuint)idx);
                    var vs = Vector256.LoadUnsafe(ref sigRef, (nuint)idx);
                    vsum += vw * vs;
                }
                acc += Vector256.Sum(vsum);
            }
            for (; idx < lastH; idx++)
                acc += Unsafe.Add(ref headWRef, idx) * Unsafe.Add(ref sigRef, idx);

            output[t] = acc;
        }
    }

    // =========================================================================
    // AVX2 / FMA 공식 NAM ConvNet (Folded BatchNorm) 실시간 추론 커널
    // =========================================================================
    private static void RunConvNet(ConvNetRuntimeState ws, ReadOnlySpan<float> input, Span<float> output)
    {
        var model = ws.Model;
        var layers = model.Layers;
        var lStates = ws.Layers;
        int nLayers = layers.Length;
        int frames = input.Length;
        ref float headWRef = ref MemoryMarshal.GetArrayDataReference(model.HeadW);
        float headB = model.HeadB;

        ref float yRef = ref MemoryMarshal.GetArrayDataReference(ws.YBuf);
        ref float nextYRef = ref MemoryMarshal.GetArrayDataReference(ws.NextYBuf);

        for (int t = 0; t < frames; t++)
        {
            yRef = input[t];

            for (int li = 0; li < nLayers; li++)
            {
                var layer = layers[li];
                var st = lStates[li];
                int inCh = layer.InChannels;
                int padIn = layer.PadInChannels;
                int outCh = layer.OutChannels;
                int padOut = layer.PadOutChannels;
                int kSize = layer.KernelSize;
                int dil = layer.Dilation;
                int wPos = st.WritePos;
                int ringMask = layer.RingMask;

                ref float ringBase = ref MemoryMarshal.GetArrayDataReference(st.Ring);
                CopyVec(ref Unsafe.Add(ref ringBase, wPos * padIn), ref yRef, padIn);

                ref float convW = ref MemoryMarshal.GetArrayDataReference(layer.ConvW);
                ref float convB = ref MemoryMarshal.GetArrayDataReference(layer.ConvB);
                CopyVec(ref nextYRef, ref convB, padOut);

                int wOff = 0;
                for (int k = 0; k < kSize; k++)
                {
                    int slot = (wPos - (kSize - 1 - k) * dil) & ringMask;
                    ref float rRow = ref Unsafe.Add(ref ringBase, slot * padIn);
                    for (int ic = 0; ic < inCh; ic++)
                    {
                        float v = Unsafe.Add(ref rRow, ic);
                        MacRow(ref nextYRef, ref Unsafe.Add(ref convW, wOff), v, padOut);
                        wOff += padOut;
                    }
                }

                ActivatePlain(ref nextYRef, ref yRef, outCh, layer.Act);
                st.WritePos = (wPos + 1) & ringMask;
            }

            int lastCh = layers[nLayers - 1].OutChannels;
            float acc = headB;
            int idx = 0;
            if (Vector256.IsHardwareAccelerated && lastCh >= 8)
            {
                var vsum = Vector256<float>.Zero;
                for (; idx <= lastCh - 8; idx += 8)
                {
                    var vw = Vector256.LoadUnsafe(ref headWRef, (nuint)idx);
                    var vs = Vector256.LoadUnsafe(ref yRef, (nuint)idx);
                    vsum += vw * vs;
                }
                acc += Vector256.Sum(vsum);
            }
            for (; idx < lastCh; idx++)
                acc += Unsafe.Add(ref headWRef, idx) * Unsafe.Add(ref yRef, idx);

            output[t] = acc;
        }
    }

    // =========================================================================
    // AVX2 / FMA Shared Vector Primitives
    // =========================================================================
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Pad8(int n) => (n + 7) & ~7;

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
}
