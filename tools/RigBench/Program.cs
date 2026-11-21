using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NAudio.Wave;
using NeuralRigStudio.Audio;

Console.WriteLine($"=== NeuralRig Studio Standalone AVX2/FMA & White-Box Benchmark (AVX2={Avx2.IsSupported}, FMA={Fma.IsSupported}, Vec256={Vector256.IsHardwareAccelerated}) ===");

// 1) Official NAM WaveNet presets (Nano / Feather / Standard)
BenchmarkWaveNetPreset("Nano (12ch x 10 gated)", new[] { (1, 12, 1, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }, true) });
var profFeather = BenchmarkWaveNetPreset("Feather (12ch x 10 + 8ch x 8 gated)", new[]
{
    (1, 12, 8, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }, false),
    (12, 8, 1, new[] { 1, 2, 4, 8, 16, 32, 64, 128 }, true),
});
BenchmarkWaveNetPreset("Standard (16ch x 10 + 8ch x 10 gated)", new[]
{
    (1, 16, 8, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }, false),
    (16, 8, 1, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }, true),
});

// 2) Native C# .nam Export -> Import round-trip verification (zero Python)
VerifyNativeNamRoundTrip(profFeather);

// 3) External Cab IR loader & clear verification
{
    var proc = new ToneProcessor();
    proc.Prepare(256, 44100);
    float[] left = new float[256];
    float[] right = new float[256];
    for (int i = 0; i < 256; i++) left[i] = right[i] = 0.2f * MathF.Sin(2f * MathF.PI * 220f * i / 44100f);

    string tmpIr = Path.Combine(Path.GetTempPath(), $"nrs_test_ir_{Guid.NewGuid():N}.wav");
    try
    {
        using (var w = new WaveFileWriter(tmpIr, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
        {
            var irSamples = new float[512];
            irSamples[0] = 0.85f;
            for (int i = 1; i < irSamples.Length; i++)
                irSamples[i] = MathF.Exp(-i * 0.03f) * 0.15f;
            w.WriteSamples(irSamples, 0, irSamples.Length);
        }
        var (irName, taps) = proc.LoadCustomCabIr(tmpIr);
        if (!proc.HasCustomCabIr || taps < 8 || string.IsNullOrEmpty(irName))
            throw new Exception("LoadCustomCabIr failed!");
        proc.Mode = ToneMode.CabIrOnly;
        proc.Process(left, right, 256);
        proc.ClearCustomCabIr();
        if (proc.HasCustomCabIr)
            throw new Exception("ClearCustomCabIr failed!");
        Console.WriteLine($"[PASS] External Cab IR load/resample(48k->44.1k)/clear verified ({taps} taps)");
    }
    finally
    {
        try { if (File.Exists(tmpIr)) File.Delete(tmpIr); } catch { }
    }
}

// 4) Power-of-2 RingBuffer integrity
{
    var rb = new RingBuffer(1024);
    var chL = new float[200];
    var chR = new float[200];
    var dstOut = new float[400];
    for (int round = 0; round < 10; round++)
    {
        for (int i = 0; i < 200; i++)
        {
            chL[i] = round * 1000f + i;
            chR[i] = -chL[i];
        }
        rb.Write(200, chL, chR);
        int got = rb.ReadInto(dstOut, 200, trimCeil: rb.CapacityFrames);
        if (got != 200) throw new Exception($"RingBuffer mismatch: {got}");
        for (int i = 0; i < 200; i++)
        {
            if (MathF.Abs(dstOut[i * 2] - chL[i]) > 1e-6f || MathF.Abs(dstOut[i * 2 + 1] - chR[i]) > 1e-6f)
                throw new Exception($"RingBuffer corruption at round {round}, idx {i}");
        }
    }
    Console.WriteLine($"[PASS] Power-of-2 Lock-Free RingBuffer (cap={rb.CapacityFrames}fr) 10x wrap-around integrity");

    // 4.1) Zero-Allocation 44.1kHz -> 48kHz Real-Time Sample Rate Bridge (GP-100 -> PC WASAPI)
    var rbBridge = new RingBuffer(8192);
    var srcBridge = new RingSource(rbBridge, sampleRate: 44100, targetFrames: 512, inChunkFrames: 256, outputSampleRate: 48000);
    if (srcBridge.WaveFormat.SampleRate != 48000)
        throw new Exception($"Expected 48000Hz bridge format, got {srcBridge.WaveFormat.SampleRate}");
    var inL44 = new float[256];
    var inR44 = new float[256];
    var outBytes48 = new byte[480 * 2 * sizeof(float)];
    var outLeft48 = new float[480 * 20];
    int inSamplePos = 0;

    void PushAndPull(int blocks, int outStartBlock)
    {
        for (int b = 0; b < blocks; b++)
        {
            for (int i = 0; i < 256; i++, inSamplePos++)
            {
                float v = 0.3f * MathF.Sin(2f * MathF.PI * 440f * inSamplePos / 44100f);
                inL44[i] = v;
                inR44[i] = v;
            }
            rbBridge.Write(256, inL44, inR44);
            while (rbBridge.FillFrames >= 768 && (outStartBlock < 0 || b >= outStartBlock))
            {
                srcBridge.Read(outBytes48);
                if (outStartBlock >= 0)
                {
                    var fs = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(outBytes48);
                    int idx = (b - outStartBlock) * 480;
                    if (idx + 480 <= outLeft48.Length)
                        for (int k = 0; k < 480; k++) outLeft48[idx + k] = fs[k * 2];
                }
                break;
            }
        }
    }

    PushAndPull(6, -1); // prefill & JIT warmup
    long bridgeAlloc0 = GC.GetAllocatedBytesForCurrentThread();
    PushAndPull(20, 0);
    long bridgeAlloc = GC.GetAllocatedBytesForCurrentThread() - bridgeAlloc0;
    if (bridgeAlloc != 0) throw new Exception($"RingSource 44.1k->48k bridge allocated {bridgeAlloc} B!");

    double bridgePeak = 0.0;
    for (int i = 0; i < outLeft48.Length; i++)
    {
        if (!float.IsFinite(outLeft48[i])) throw new Exception("Non-finite sample from 44.1k->48k bridge!");
        bridgePeak = Math.Max(bridgePeak, Math.Abs(outLeft48[i]));
    }
    if (bridgePeak < 0.25) throw new Exception($"Unexpected low output amplitude from 44.1k->48k bridge: {bridgePeak}");
    Console.WriteLine($"[PASS] 44.1kHz->48kHz Zero-Alloc Sample-Rate Bridge (GP-100 -> WASAPI, peak={bridgePeak:F3}, RT alloc: {bridgeAlloc} B)");
}

// 5) Radix-2 FFT + YIN Chromatic Tuner verification
{
    float[] re = new float[1024];
    float[] im = new float[1024];
    for (int i = 0; i < 1024; i++)
    {
        float hann = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / 1023f));
        re[i] = hann * MathF.Sin(2f * MathF.PI * 1000f * i / 44100f);
    }
    VisualizerAnalyzer.FftInPlace(re, im);
    int peakBin = 0;
    float maxMag = -1f;
    for (int k = 1; k < 512; k++)
    {
        float m = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]);
        if (m > maxMag) { maxMag = m; peakBin = k; }
    }
    double peakHz = peakBin * 44100.0 / 1024.0;
    Console.WriteLine($"[PASS] Radix-2 FFT 1000Hz tone -> Peak bin {peakBin} ({peakHz:F1} Hz)");
    if (Math.Abs(peakHz - 1000.0) > 50.0) throw new Exception($"FFT peak off: {peakHz}");

    var vis = new VisualizerAnalyzer();
    float[] dryBuf = new float[VisualizerAnalyzer.TunerN];
    foreach (var (targetHz, expNote, expOctave) in new[] { (110f, "A", 2), (440f, "A", 4), (329.63f, "E", 4) })
    {
        for (int i = 0; i < dryBuf.Length; i++)
            dryBuf[i] = 0.25f * MathF.Sin(2f * MathF.PI * targetHz * i / 44100f);
        var t = vis.DetectPitchYin(dryBuf, 44100);
        if (!t.Active || t.NoteName != expNote || t.Octave != expOctave || MathF.Abs(t.Cents) > 2.5f)
            throw new Exception($"YIN Tuner mismatch for {targetHz}Hz: got {t}");
        Console.WriteLine($"[PASS] YIN Chromatic Tuner {targetHz}Hz -> {t.NoteName}{t.Octave} ({t.FreqHz:F1}Hz, {t.Cents:+0.0;-0.0;0.0} ct)");
    }
}

// 6) ASIO Driver enumeration
var drivers = AudioRouter.GetDriverNames();
Console.WriteLine($"[PASS] ASIO drivers detected ({drivers.Length}): {string.Join(", ", drivers)}");

// 7) WhiteBox Circuit Modeling + Hybrid Routing Quantitative Verification
RunWhiteBoxVerification();

static ToneProfile BenchmarkWaveNetPreset(string label, (int inSz, int ch, int headSz, int[] dils, bool headBias)[] specs)
{
    var cfg = MakeTestWaveNetConfig(specs, out var flatWeights);
    var p = new ToneProfile
    {
        Id = "bench-wn",
        Name = label,
        Version = 3,
        Architecture = "WaveNet",
        Esr = 0.0042,
        SampleRate = 44100,
        WaveNetConfig = cfg,
        Weights = flatWeights,
    };
    p.PrepareForRt();
    if (!p.IsWaveNet) throw new Exception($"Failed to compile WaveNet preset: {label}");

    var proc = new ToneProcessor
    {
        ActiveProfile = p,
        Mode = ToneMode.FullRig,
        DriveMultiplier = 1.0f,
        WetMix = 1.0f,
        OutputGainDb = 0f,
        GateThresholdDb = -80f,
    };
    const int frames = 256;
    proc.Prepare(frames, 44100);
    var l = new float[frames];
    var r = new float[frames];
    for (int i = 0; i < frames; i++) l[i] = r[i] = 0.25f * MathF.Sin(2f * MathF.PI * 196f * i / 44100f);
    for (int w = 0; w < 30; w++) proc.Process(l, r, frames);

    const int iters = 500;
    var sw = new Stopwatch();
    long allocBefore = GC.GetAllocatedBytesForCurrentThread();
    sw.Restart();
    for (int k = 0; k < iters; k++)
    {
        for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i);
        proc.Process(l, r, frames);
    }
    sw.Stop();
    long allocBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

    for (int i = 0; i < frames; i++)
        if (!float.IsFinite(l[i]) || !float.IsFinite(r[i]))
            throw new Exception($"Non-finite WaveNet output in {label}!");

    double perBlockMs = sw.Elapsed.TotalMilliseconds / iters;
    double budgetMs = frames * 1000.0 / 44100.0;
    Console.WriteLine($"[PASS] WaveNet {label} (256fr): {perBlockMs:F4} ms / block (Budget: {budgetMs:F2} ms, CPU: {perBlockMs / budgetMs * 100:F2}%, RT alloc: {allocBytes} B)");
    if (perBlockMs >= budgetMs) throw new Exception($"WaveNet {label} exceeded 5.80ms RT budget!");
    if (allocBytes != 0) throw new Exception($"WaveNet {label} allocated {allocBytes} bytes on RT path!");
    return p;
}

static void VerifyNativeNamRoundTrip(ToneProfile sourceProf)
{
    string tmpNam = Path.Combine(Path.GetTempPath(), $"nrs_roundtrip_{Guid.NewGuid():N}.nam");
    ToneProfile? imported = null;
    try
    {
        ToneProfileStore.ExportNamFile(sourceProf, tmpNam);
        imported = ToneProfileStore.ImportNamFile(tmpNam, "RoundTripCheck");
        if (!imported.IsWaveNet || imported.ArchPreset != "feather")
            throw new Exception($"Unexpected imported NAM metadata: IsWaveNet={imported.IsWaveNet}, preset={imported.ArchPreset}");

        var probe = new float[2048];
        for (int i = 0; i < probe.Length; i++)
            probe[i] = 0.35f * MathF.Sin(2f * MathF.PI * 147f * i / 44100f) + 0.15f * MathF.Cos(2f * MathF.PI * 523f * i / 44100f);

        var pOrig = new ToneProcessor { ActiveProfile = sourceProf, Mode = ToneMode.FullRig };
        var pImp = new ToneProcessor { ActiveProfile = imported, Mode = ToneMode.FullRig };
        pOrig.Prepare(256, 44100);
        pImp.Prepare(256, 44100);

        var outOrig = new float[probe.Length];
        var outImp = new float[probe.Length];

        // Warm up ProcessWaveNetRaw JIT / reset before zero-alloc measurement (same as BenchmarkWaveNetPreset)
        pOrig.ProcessWaveNetRaw(probe.AsSpan(0, 256), outOrig.AsSpan(0, 256));
        pOrig.ResetState();
        pImp.ResetState();

        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (int off = 0; off < probe.Length; off += 256)
        {
            pOrig.ProcessWaveNetRaw(probe.AsSpan(off, 256), outOrig.AsSpan(off, 256));
            pImp.ProcessWaveNetRaw(probe.AsSpan(off, 256), outImp.AsSpan(off, 256));
        }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;

        double maxDiff = 0.0;
        for (int i = 0; i < probe.Length; i++)
            maxDiff = Math.Max(maxDiff, Math.Abs(outOrig[i] - outImp[i]));

        Console.WriteLine($"[PASS] Native C# .nam Export->Import round-trip: max_abs_diff={maxDiff:E2}, ESR={imported.EsrDb:F2}dB, RT alloc={alloc} B");
        if (maxDiff > 1e-6) throw new Exception($"NAM round-trip diff too large: {maxDiff}");
        if (alloc != 0) throw new Exception($"NAM streaming allocated {alloc} B!");
    }
    finally
    {
        if (imported != null)
            ToneProfileStore.Delete(imported);
        try { if (File.Exists(tmpNam)) File.Delete(tmpNam); } catch { }
    }
}

static WaveNetConfigDto MakeTestWaveNetConfig((int inSz, int ch, int headSz, int[] dils, bool headBias)[] specs, out float[] flatWeights)
{
    var cfg = new WaveNetConfigDto { HeadScale = 0.02f };
    var rng = new Random(42);
    var weights = new List<float>();

    foreach (var (inSz, ch, headSz, dils, headBias) in specs)
    {
        cfg.Layers.Add(new WaveNetLayerArrayCfg
        {
            InputSize = inSz,
            ConditionSize = 1,
            HeadSize = headSz,
            Channels = ch,
            KernelSize = 3,
            Dilations = dils,
            Gated = true,
            HeadBias = headBias,
        });

        int reCount = ch * inSz;
        for (int i = 0; i < reCount; i++) weights.Add((float)(rng.NextDouble() * 0.2 - 0.1));
        for (int li = 0; li < dils.Length; li++)
        {
            int mid = 2 * ch;
            for (int i = 0; i < mid * ch * 3; i++) weights.Add((float)(rng.NextDouble() * 0.1 - 0.05));
            for (int i = 0; i < mid; i++) weights.Add(0f);
            for (int i = 0; i < mid; i++) weights.Add((float)(rng.NextDouble() * 0.1 - 0.05));
            for (int i = 0; i < ch * ch; i++) weights.Add((float)(rng.NextDouble() * 0.1 - 0.05));
            for (int i = 0; i < ch; i++) weights.Add(0f);
        }
        for (int i = 0; i < headSz * ch; i++) weights.Add((float)(rng.NextDouble() * 0.1 - 0.05));
        if (headBias)
            for (int i = 0; i < headSz; i++) weights.Add(0f);
    }
    weights.Add(0.02f);
    flatWeights = weights.ToArray();
    return cfg;
}

static void RunWhiteBoxVerification()
{
    const int sr = 44100;
    const int frames = 256;
    var wb = new WhiteBoxAmpEngine();
    wb.Prepare(2048, sr);

    static float GoertzelMag(ReadOnlySpan<float> buf, double freqHz, int sampleRate)
    {
        double w = 2.0 * Math.PI * freqHz / sampleRate;
        double coeff = 2.0 * Math.Cos(w);
        double q1 = 0.0, q2 = 0.0;
        for (int i = 0; i < buf.Length; i++)
        {
            double win = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (buf.Length - 1)));
            double q0 = coeff * q1 - q2 + buf[i] * win;
            q2 = q1;
            q1 = q0;
        }
        double re = q1 - q2 * Math.Cos(w);
        double im = q2 * Math.Sin(w);
        return (float)(Math.Sqrt(re * re + im * im) * 4.0 / buf.Length);
    }

    static double Rms(ReadOnlySpan<float> s)
    {
        double sum = 0.0;
        for (int i = 0; i < s.Length; i++) sum += (double)s[i] * s[i];
        return Math.Sqrt(sum / Math.Max(1, s.Length));
    }

    // 7.1) 4x HIIR passband & anti-aliasing
    var lb = new float[2048];
    for (int i = 0; i < lb.Length; i++) lb[i] = MathF.Sin(2f * MathF.PI * 1000f * i / sr);
    wb.Reset();
    wb.TestOversamplerLoopback(lb);
    double pbDb = 20.0 * Math.Log10(Math.Max(1e-9, Rms(lb.AsSpan(512))) / Math.Sqrt(0.5));
    if (Math.Abs(pbDb) > 0.1) throw new Exception($"HIIR 4x passband error {pbDb:F3} dB > 0.1 dB");

    static float HardClipShaper(float x) => MathF.Tanh(x);
    var s1x = new float[2048];
    var s4x = new float[2048];
    for (int i = 0; i < 2048; i++)
    {
        float v = 4.0f * MathF.Sin(2f * MathF.PI * 7500f * i / sr);
        s1x[i] = HardClipShaper(v);
        s4x[i] = v;
    }
    wb.Reset();
    wb.TestOversampledNonlinear(s4x, HardClipShaper);
    double alias1x = GoertzelMag(s1x.AsSpan(512), 6600.0, sr) + GoertzelMag(s1x.AsSpan(512), 8400.0, sr);
    double alias4x = GoertzelMag(s4x.AsSpan(512), 6600.0, sr) + GoertzelMag(s4x.AsSpan(512), 8400.0, sr);
    double suppressDb = 20.0 * Math.Log10(Math.Max(1e-12, alias1x) / Math.Max(1e-12, alias4x));
    Console.WriteLine($"[PASS] 4x HIIR Oversampler: 1kHz passband err={pbDb:+0.000;-0.000;0.000} dB, 7.5kHz aliasing suppression={suppressDb:F1} dB");
    if (suppressDb < 40.0) throw new Exception($"Anti-aliasing suppression {suppressDb:F1} dB < 40 dB");

    // 7.2) TS808 diode clipper
    wb.UpdateParams(new WhiteBoxParams { TsEnabled = true, TsDrive = 7.0f, TsTone = 5.5f, TsLevel = 5.0f });
    wb.Reset();
    var tsLow = new float[2048];
    var tsMid = new float[2048];
    for (int i = 0; i < 2048; i++)
    {
        tsLow[i] = 0.15f * MathF.Sin(2f * MathF.PI * 100f * i / sr);
        tsMid[i] = 0.15f * MathF.Sin(2f * MathF.PI * 1000f * i / sr);
    }
    wb.TestTs808Only(tsLow);
    wb.Reset();
    wb.TestTs808Only(tsMid);
    double tsMidVsLowDb = 20.0 * Math.Log10(Rms(tsMid.AsSpan(512)) / Math.Max(1e-9, Rms(tsLow.AsSpan(512))));
    Console.WriteLine($"[PASS] TS808 4x Diode Clipper (Drive=7): 1kHz vs 100Hz = {tsMidVsLowDb:+0.0;-0.0} dB");
    if (tsMidVsLowDb < 2.0) throw new Exception($"TS808 mid-hump too small: {tsMidVsLowDb:F2} dB");

    // 7.3) 12AX7 3-channel THD
    double[] thds = new double[3];
    double hgEvenRatio = 0.0;
    for (int ch = 0; ch < 3; ch++)
    {
        wb.UpdateParams(new WhiteBoxParams { Channel = (WhiteBoxChannel)ch, PreGain = 6.5f, TsEnabled = false });
        wb.Reset();
        var sig = new float[2048];
        for (int i = 0; i < 2048; i++) sig[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
        wb.TestPreampOnly(sig);
        double f1 = GoertzelMag(sig.AsSpan(512), 220.0, sr);
        double f2 = GoertzelMag(sig.AsSpan(512), 440.0, sr);
        double f3 = GoertzelMag(sig.AsSpan(512), 660.0, sr);
        double f4 = GoertzelMag(sig.AsSpan(512), 880.0, sr);
        double f5 = GoertzelMag(sig.AsSpan(512), 1100.0, sr);
        thds[ch] = Math.Sqrt(f2 * f2 + f3 * f3 + f4 * f4 + f5 * f5) / Math.Max(1e-9, f1) * 100.0;
        if (ch == 2) hgEvenRatio = f2 / Math.Max(1e-9, f1) * 100.0;
    }
    Console.WriteLine($"[PASS] 12AX7 Preamp THD: Clean={thds[0]:F1}% < Plexi={thds[1]:F1}% < HighGain={thds[2]:F1}% (2nd harm={hgEvenRatio:F1}%)");
    if (!(thds[0] < thds[1] && thds[1] < thds[2]) || hgEvenRatio < 3.0)
        throw new Exception("12AX7 channel THD or even-harmonic failed!");

    // 7.4) FMV 4-Node Passive Stack
    double MeasureFmvDb(WhiteBoxChannel ch, float b, float m, float t, double freqHz)
    {
        wb.UpdateParams(new WhiteBoxParams { Channel = ch, Bass = b, Mid = m, Treble = t });
        wb.Reset();
        var s = new float[2048];
        for (int i = 0; i < 2048; i++) s[i] = 0.5f * MathF.Sin(2f * MathF.PI * (float)freqHz * i / sr);
        wb.TestFmvOnly(s);
        for (int i = 0; i < s.Length; i++)
            if (!float.IsFinite(s[i])) throw new Exception($"Non-finite FMV at {b}-{m}-{t}!");
        return 20.0 * Math.Log10(Math.Max(1e-9, Rms(s.AsSpan(512))) / (0.5 * Math.Sqrt(0.5)));
    }
    double bf100 = MeasureFmvDb(WhiteBoxChannel.Clean, 5, 5, 5, 100);
    double bf500 = MeasureFmvDb(WhiteBoxChannel.Clean, 5, 5, 5, 500);
    double bf4k = MeasureFmvDb(WhiteBoxChannel.Clean, 5, 5, 5, 4000);
    double midDelta = MeasureFmvDb(WhiteBoxChannel.Clean, 5, 10, 5, 500) - MeasureFmvDb(WhiteBoxChannel.Clean, 5, 0, 5, 500);
    double bassDelta = MeasureFmvDb(WhiteBoxChannel.Clean, 10, 5, 5, 100) - MeasureFmvDb(WhiteBoxChannel.Clean, 0, 5, 5, 100);
    double trebDelta = MeasureFmvDb(WhiteBoxChannel.Clean, 5, 5, 10, 4000) - MeasureFmvDb(WhiteBoxChannel.Clean, 5, 5, 0, 4000);
    _ = MeasureFmvDb(WhiteBoxChannel.Clean, 0, 0, 0, 1000);
    Console.WriteLine($"[PASS] FMV 4-Node Passive Stack (BF 5-5-5 scoop={bf500:F1}dB): dBass={bassDelta:F1}dB, dMid={midDelta:F1}dB, dTreble={trebDelta:F1}dB");
    if (bf500 >= bf100 || bf500 >= bf4k || midDelta < 5.0 || bassDelta < 6.0 || trebDelta < 8.0)
        throw new Exception("FMV tone stack verification failed!");

    // 7.5) PowerAmp Sag / Presence / Depth
    double MeasurePowerSustDb(float sag)
    {
        wb.UpdateParams(new WhiteBoxParams { Master = 7.5f, Sag = sag, Presence = 5f, Depth = 5f });
        wb.Reset();
        int len = sr / 2;
        var buf = new float[len];
        for (int i = 0; i < len; i++) buf[i] = 0.85f * MathF.Sin(2f * MathF.PI * 110f * i / sr);
        for (int off = 0; off < len; off += 512)
            wb.TestPowerAmpOnly(buf.AsSpan(off, Math.Min(512, len - off)));
        return 20.0 * Math.Log10(Rms(buf.AsSpan(0, 600)) / Math.Max(1e-9, Rms(buf.AsSpan(6615, 4410))));
    }
    double sagDrop0 = MeasurePowerSustDb(0f);
    double sagDrop9 = MeasurePowerSustDb(9f);
    double MeasurePowerEqDb(float pres, float depth, double fHz)
    {
        wb.UpdateParams(new WhiteBoxParams { Master = 3.0f, Sag = 0f, Presence = pres, Depth = depth });
        wb.Reset();
        var s = new float[2048];
        for (int i = 0; i < 2048; i++) s[i] = 0.08f * MathF.Sin(2f * MathF.PI * (float)fHz * i / sr);
        wb.TestPowerAmpOnly(s);
        return 20.0 * Math.Log10(Math.Max(1e-9, Rms(s.AsSpan(512))));
    }
    double presBoost = MeasurePowerEqDb(10f, 5f, 4000.0) - MeasurePowerEqDb(0f, 5f, 4000.0);
    double depthBoost = MeasurePowerEqDb(5f, 10f, 100.0) - MeasurePowerEqDb(5f, 0f, 100.0);
    Console.WriteLine($"[PASS] PowerAmp Sag/Presence/Depth: SagDrop(9)={sagDrop9 - sagDrop0:F2}dB, Presence={presBoost:F1}dB, Depth={depthBoost:F1}dB");
    if (sagDrop9 - sagDrop0 < 0.5 || presBoost < 4.0 || depthBoost < 4.0)
        throw new Exception("PowerAmp verification failed!");

    // 7.6) WhiteBoxRig & HybridPreampNam (Feather / Standard)
    var cfgFeather = MakeTestWaveNetConfig(new[]
    {
        (1, 12, 8, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }, false),
        (12, 8, 1, new[] { 1, 2, 4, 8, 16, 32, 64, 128 }, true),
    }, out var wFeather);
    var cfgStd = MakeTestWaveNetConfig(new[]
    {
        (1, 16, 8, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }, false),
        (16, 8, 1, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }, true),
    }, out var wStd);

    var pFeather = new ToneProfile { Id = "hyb-f", Name = "HybridFeather", Version = 3, Architecture = "WaveNet", SampleRate = sr, WaveNetConfig = cfgFeather, Weights = wFeather };
    pFeather.PrepareForRt();
    var pStd = new ToneProfile { Id = "hyb-s", Name = "HybridStd", Version = 3, Architecture = "WaveNet", SampleRate = sr, WaveNetConfig = cfgStd, Weights = wStd };
    pStd.PrepareForRt();

    void BenchMode(string label, ToneMode mode, ToneProfile? p, bool powerAmp)
    {
        var tp = new ToneProcessor { ActiveProfile = p, Mode = mode, DriveMultiplier = 1f, WetMix = 1f, GateThresholdDb = -80f };
        tp.Prepare(frames, sr);
        tp.WhiteBox.UpdateParams(new WhiteBoxParams
        {
            Channel = WhiteBoxChannel.HighGain,
            Bright = true,
            TsEnabled = true,
            TsDrive = 5.5f,
            PreGain = 6.5f,
            Bass = 5f,
            Mid = 5f,
            Treble = 6f,
            Master = 5.5f,
            Sag = 4f,
            Presence = 6f,
            Depth = 5.5f,
            PowerAmpEnabled = powerAmp,
        });
        var l = new float[frames];
        var r = new float[frames];
        for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
        for (int w = 0; w < 30; w++) tp.Process(l, r, frames);

        const int iters = 400;
        var sw = new Stopwatch();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int k = 0; k < iters; k++)
        {
            for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i);
            tp.Process(l, r, frames);
        }
        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;

        for (int i = 0; i < frames; i++)
            if (!float.IsFinite(l[i]) || !float.IsFinite(r[i]))
                throw new Exception($"Non-finite output in {label}!");

        double ms = sw.Elapsed.TotalMilliseconds / iters;
        double budget = frames * 1000.0 / sr;
        Console.WriteLine($"[PASS] {label} (256fr): {ms:F4} ms / block (Budget: {budget:F2} ms, CPU: {ms / budget * 100:F2}%, RT alloc: {alloc} B)");
        if (ms >= budget) throw new Exception($"{label} exceeded {budget:F2} ms budget!");
        if (alloc != 0) throw new Exception($"{label} allocated {alloc} bytes on RT path!");
    }

    BenchMode("WhiteBoxRig (4x+TS808+HighGain+FMV+PowerAmp+512Cab)", ToneMode.WhiteBoxRig, null, true);
    BenchMode("HybridPreampNam (4x WhiteBox + Feather WaveNet)", ToneMode.HybridPreampNam, pFeather, false);
    BenchMode("HybridPreampNam (4x WhiteBox + Standard WaveNet)", ToneMode.HybridPreampNam, pStd, false);

    // 7.7) Modular SignalChainEngine (Quad Cortex 2x8 full pedalboard + parallel split/merge + stereo FX)
    {
        var tp = new ToneProcessor { Mode = ToneMode.SignalChain };
        tp.Prepare(frames, sr);
        var cfg = new SignalChainConfigDto
        {
            RoutingMode = (int)ChainRoutingMode.ParallelSplit,
            SplitCol = 2,
            MergeCol = 5,
            ParallelMix = 0.45f,
        };
        // Enable Row 2 parallel EQ + CE-2 Chorus in addition to Row 1 full rig
        cfg.Slots[8].Enabled = true;
        cfg.Slots[9].Enabled = true;
        cfg.Slots[4] = new ChainSlotDto { Index = 4, ModelId = "od_klon", Enabled = true, Params = new float[] { 4.5f, 5.5f, 5.0f, 0, 0, 0, 0, 0 } };
        tp.Chain.UpdateChain(cfg);

        var l = new float[frames];
        var r = new float[frames];
        for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
        for (int w = 0; w < 30; w++) tp.Process(l, r, frames);

        const int iters = 400;
        var sw = new Stopwatch();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int k = 0; k < iters; k++)
        {
            for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i);
            tp.Process(l, r, frames);
        }
        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;

        bool hasStereoDiff = false;
        for (int i = 0; i < frames; i++)
        {
            if (!float.IsFinite(l[i]) || !float.IsFinite(r[i]))
                throw new Exception("Non-finite output in SignalChain!");
            if (MathF.Abs(l[i] - r[i]) > 1e-5f) hasStereoDiff = true;
        }
        double ms = sw.Elapsed.TotalMilliseconds / iters;
        double budget = frames * 1000.0 / sr;
        Console.WriteLine($"[PASS] SignalChain 2x8 Full Rig + Parallel Stereo FX (256fr): {ms:F4} ms / block (Budget: {budget:F2} ms, CPU: {ms / budget * 100:F2}%, Stereo={hasStereoDiff}, RT alloc: {alloc} B)");
        if (ms >= budget) throw new Exception($"SignalChain exceeded {budget:F2} ms budget!");
        if (alloc != 0) throw new Exception($"SignalChain allocated {alloc} bytes on RT path!");
        if (!hasStereoDiff) throw new Exception("SignalChain stereo FX did not produce stereo width!");

        // BOSS Pedals full-board zero-alloc check
        var bossCfg = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
        string[] bossIds = { "comp_cs3", "od_bd2", "dist_ds1", "dist_mt2", "dist_hm2", "eq_ge7", "mod_dc2w", "mod_bf2", "dly_dm2w", "dly_dd3t" };
        for (int i = 0; i < bossIds.Length; i++)
            bossCfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = bossIds[i], Enabled = true, Params = new float[] { 5f, 5f, 5f, 5f, 1000f, 5f, 0f, 0f } };
        tp.Chain.UpdateChain(bossCfg);
        for (int w = 0; w < 10; w++) tp.Process(l, r, frames);
        long bAlloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 200; k++)
        {
            for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i);
            tp.Process(l, r, frames);
        }
        long bAlloc = GC.GetAllocatedBytesForCurrentThread() - bAlloc0;
        for (int i = 0; i < frames; i++)
            if (!float.IsFinite(l[i]) || !float.IsFinite(r[i]))
                throw new Exception("Non-finite output in BOSS pedalboard!");
        Console.WriteLine($"[PASS] BOSS 10-Pedal Board (CS-3/BD-2/DS-1/MT-2/HM-2/GE-7/DC-2W/BF-2/DM-2W/DD-3T) RT alloc: {bAlloc} B");
        if (bAlloc != 0) throw new Exception($"BOSS pedalboard allocated {bAlloc} bytes on RT path!");

        // J-Pop / J-Rock Boutique Pedals + Amps + Cabs + Strymon blueSky Shimmer zero-alloc check
        var jpCfg = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
        string[] jpIds =
        {
            "comp_sp", "od_puresky", "od_janray", "od_ts9", "dist_rat2",
            "od_bbpreamp", "od_ocd", "dist_ecstasy", "boost_microamp",
            "amp_jc120", "cab_jc120", "amp_ac30", "cab_alnico", "dly_timeline", "rev_bluesky"
        };
        for (int i = 0; i < jpIds.Length; i++)
            jpCfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = jpIds[i], Enabled = true, Params = new float[] { 55f, 5f, 5f, 35f, 35f, 5f, 5f, 1f } };
        tp.Chain.UpdateChain(jpCfg);
        for (int w = 0; w < 10; w++) tp.Process(l, r, frames);
        long jAlloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 200; k++)
        {
            for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i);
            tp.Process(l, r, frames);
        }
        long jAlloc = GC.GetAllocatedBytesForCurrentThread() - jAlloc0;
        for (int i = 0; i < frames; i++)
            if (!float.IsFinite(l[i]) || !float.IsFinite(r[i]))
                throw new Exception("Non-finite output in J-Pop/J-Rock rig!");
        Console.WriteLine($"[PASS] J-Pop/J-Rock 15-Block Rig (SP/PureSky/JanRay/TS9/RAT2/OCD/BB/Ecstasy/JC120/AC30/TimeLine/blueSky) RT alloc: {jAlloc} B");
        if (jAlloc != 0) throw new Exception($"J-Pop/J-Rock rig allocated {jAlloc} bytes on RT path!");

        // 7.8) Exhaustive 72-Device Catalog Zero-Alloc & Finite Output Audit
        string[] allCatalogIds =
        {
            // AMP (11)
            "amp_plexi", "amp_2203", "amp_twin", "amp_1star", "amp_slo100", "amp_5153",
            "amp_jc120", "amp_ac30", "amp_dsl", "amp_recto", "amp_be100",
            // NAM (1)
            "nam_capture",
            // CAB (8)
            "cab_v30", "cab_custom", "cab_jc120", "cab_alnico", "cab_1960a", "cab_1960bv", "cab_mesa412", "cab_jensen112",
            // DRIVE / DIST / FUZZ / BOOST (25)
            "od_ts808", "od_ts9", "od_janray", "od_puresky", "od_bbpreamp", "od_ocd", "od_bluesbreaker", "od_boxofrock",
            "dist_rat2", "dist_ecstasy", "dist_guvnor", "dist_plus", "boost_microamp", "boost_rangemaster", "od_klon", "dist_rat", "fuzz_bigmuff", "fuzz_face",
            "od_sd1", "od_bd2", "dist_ds1", "dist_mt2", "dist_hm2", "boost_ep", "boost_rc",
            // DELAY (5)
            "dly_dm2w", "dly_dd3t", "dly_analog", "dly_digital", "dly_timeline",
            // REVERB (5)
            "rev_bluesky", "rev_rv6", "rev_spring", "rev_hall", "rev_plate",
            // MOD (7)
            "mod_ce2", "mod_dc2w", "mod_bf2", "mod_smallclone", "mod_univibe", "mod_phase90", "mod_tremolo",
            // EQ / WAH (4)
            "eq_ge7", "eq_graphic5", "eq_parametric", "eq_crybaby",
            // DYN (6)
            "gate_adaptive", "comp_sp", "comp_orangesqueezer", "comp_cs3", "comp_dyna", "comp_1176",
        };
        static SignalChainConfigDto MakeSinglePedalChain(string modelId, float[] p)
        {
            var c = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                c.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            c.Slots[0] = new ChainSlotDto { Index = 0, ModelId = modelId, Enabled = true, Params = p };
            return c;
        }

        tp.ActiveProfile = pFeather;
        foreach (var id in allCatalogIds)
        {
            var singleCfg = MakeSinglePedalChain(id, new float[] { 5.5f, 5.5f, 5.5f, 5.5f, 950f, 5.5f, 0f, 1f });
            tp.Chain.UpdateChain(singleCfg);
            tp.ResetState();
            for (int w = 0; w < 4; w++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
                tp.Process(l, r, frames);
            }
            long cAlloc0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
                tp.Process(l, r, frames);
            }
            long cAlloc = GC.GetAllocatedBytesForCurrentThread() - cAlloc0;
            if (cAlloc != 0) throw new Exception($"Device '{id}' allocated {cAlloc} B on RT path!");
            double eOut = Rms(l);
            if (!double.IsFinite(eOut) || eOut < 1e-5)
                throw new Exception($"Device '{id}' produced invalid RMS output: {eOut}");
        }
        Console.WriteLine($"[PASS] Exhaustive {allCatalogIds.Length}-Device Catalog Audit: 100% finite output, RT alloc: 0 B");

        // 7.9) Quantitative Schematic Circuit Physics Verification (Isolated Pedal Bench)
        (double h2Pct, double thdPct) MeasurePedalHarmonics(string modelId, float[] p)
        {
            tp.Chain.UpdateChain(MakeSinglePedalChain(modelId, p));
            tp.ResetState();
            var sigL = new float[2048];
            var sigR = new float[2048];
            for (int i = 0; i < 2048; i++) sigL[i] = sigR[i] = 0.30f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
            for (int off = 0; off < 2048; off += 256)
                tp.Process(sigL.AsSpan(off, 256), sigR.AsSpan(off, 256), 256);
            double f1 = GoertzelMag(sigL.AsSpan(512), 220.0, sr);
            double f2 = GoertzelMag(sigL.AsSpan(512), 440.0, sr);
            double f3 = GoertzelMag(sigL.AsSpan(512), 660.0, sr);
            return (f2 / Math.Max(1e-9, f1) * 100.0, Math.Sqrt(f2 * f2 + f3 * f3) / Math.Max(1e-9, f1) * 100.0);
        }

        var (tsH2, tsThd) = MeasurePedalHarmonics("od_ts808", new float[] { 7f, 5.5f, 6f, 0, 0, 0, 0, 0 });
        var (sd1H2, sd1Thd) = MeasurePedalHarmonics("od_sd1", new float[] { 7f, 5.5f, 6f, 0, 0, 0, 0, 0 });
        var (jrH2, jrThd) = MeasurePedalHarmonics("od_janray", new float[] { 7f, 5f, 6f, 8f, 6f, 0, 0, 0 });
        var (psH2, psThd) = MeasurePedalHarmonics("od_puresky", new float[] { 7f, 5f, 6f, 6f, 0, 0, 0, 0 });
        Console.WriteLine($"[PASS] Schematic Diode Symmetry Physics (2nd Harm %): Symmetric(TS808={tsH2:F2}%, PureSky={psH2:F2}%) vs Asymmetric(SD-1={sd1H2:F2}%, JanRay={jrH2:F2}%)");
        if (sd1H2 <= tsH2 + 1.0 || jrH2 <= psH2 + 1.0)
            throw new Exception("Asymmetric 3-diode schematic failed to generate higher 2nd harmonic than symmetric pair!");

        double MeasurePedalFreqDb(string modelId, float[] p, double freqHz)
        {
            tp.Chain.UpdateChain(MakeSinglePedalChain(modelId, p));
            tp.ResetState();
            var sigL = new float[2048];
            var sigR = new float[2048];
            for (int i = 0; i < 2048; i++) sigL[i] = sigR[i] = 0.15f * MathF.Sin(2f * MathF.PI * (float)freqHz * i / sr);
            for (int off = 0; off < 2048; off += 256)
                tp.Process(sigL.AsSpan(off, 256), sigR.AsSpan(off, 256), 256);
            return 20.0 * Math.Log10(Math.Max(1e-9, Rms(sigL.AsSpan(512))));
        }

        double ratBright = MeasurePedalFreqDb("dist_rat2", new float[] { 5f, 2f, 6f, 0, 0, 0, 0, 0 }, 3500.0);
        double ratDark = MeasurePedalFreqDb("dist_rat2", new float[] { 5f, 9f, 6f, 0, 0, 0, 0, 0 }, 3500.0);
        double mt2Mid500 = MeasurePedalFreqDb("dist_mt2", new float[] { 4f, 0f, 0f, 12f, 500f, 6f, 0, 0 }, 500.0)
                         - MeasurePedalFreqDb("dist_mt2", new float[] { 4f, 0f, 0f, -12f, 500f, 6f, 0, 0 }, 500.0);
        Console.WriteLine($"[PASS] Schematic Tone Controls: RAT 2 Reverse Filter d3.5kHz={ratBright - ratDark:F1}dB, MT-2 Parametric Mid(500Hz) span={mt2Mid500:F1}dB");
        if (ratBright - ratDark < 3.0 || mt2Mid500 < 8.0)
            throw new Exception("Schematic tone stack verification failed!");

        // 7.10) Dual-Amp + Dual-Cab Isolation & Pre-Cab Stereo Preservation Verification
        {
            // A) Parallel Dual-Amp + Dual-Cab (JCM800+1960A || Dual Recto+Mesa 4x12)
            var dualCfg = new SignalChainConfigDto
            {
                RoutingMode = (int)ChainRoutingMode.ParallelSplit,
                SplitCol = 0,
                MergeCol = 5,
                ParallelMix = 0.5f,
            };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                dualCfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            dualCfg.Slots[1] = new ChainSlotDto { Index = 1, ModelId = "amp_2203", Enabled = true, Params = new float[] { 6.5f, 5f, 6f, 6f, 5.5f, 3.5f, 5.5f, 0f } };
            dualCfg.Slots[2] = new ChainSlotDto { Index = 2, ModelId = "cab_1960a", Enabled = true, Params = new float[] { 80f, 9800f, 0f, 0, 0, 0, 0, 0 } };
            dualCfg.Slots[8] = new ChainSlotDto { Index = 8, ModelId = "amp_recto", Enabled = true, Params = new float[] { 7.5f, 5f, 4.5f, 6.2f, 5.5f, 3f, 6f, 1f } };
            dualCfg.Slots[9] = new ChainSlotDto { Index = 9, ModelId = "cab_mesa412", Enabled = true, Params = new float[] { 85f, 9200f, 0f, 0, 0, 0, 0, 0 } };
            tp.Chain.UpdateChain(dualCfg);
            tp.ResetState();
            for (int w = 0; w < 10; w++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i);
                tp.Process(l, r, frames);
            }
            var swDual = new Stopwatch();
            long dAlloc0 = GC.GetAllocatedBytesForCurrentThread();
            swDual.Restart();
            for (int k = 0; k < 200; k++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i);
                tp.Process(l, r, frames);
            }
            swDual.Stop();
            long dAlloc = GC.GetAllocatedBytesForCurrentThread() - dAlloc0;
            double dualMs = swDual.Elapsed.TotalMilliseconds / 200.0;
            Console.WriteLine($"[PASS] Parallel Dual-Amp + Dual-Cab Isolated Pools (256fr): {dualMs:F4} ms/block, RT alloc: {dAlloc} B");
            if (dAlloc != 0 || dualMs >= frames * 1000.0 / sr)
                throw new Exception($"Dual-Amp/Cab failed: alloc={dAlloc}, ms={dualMs}");

            // B) Pre-Cab Stereo Preservation: DC-2W -> Stereo JC-120 -> Stereo 2x12 JC-120 Cab IR
            var stCfg = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                stCfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            stCfg.Slots[0] = new ChainSlotDto { Index = 0, ModelId = "mod_dc2w", Enabled = true, Params = new float[] { 3f, 70f, 0, 0, 0, 0, 0, 0 } };
            stCfg.Slots[1] = new ChainSlotDto { Index = 1, ModelId = "amp_jc120", Enabled = true, Params = new float[] { 4.5f, 5f, 5f, 6f, 5.5f, 0, 0, 1f } };
            stCfg.Slots[2] = new ChainSlotDto { Index = 2, ModelId = "cab_jc120", Enabled = true, Params = new float[] { 65f, 12500f, 0f, 0, 0, 0, 0, 0 } };
            tp.Chain.UpdateChain(stCfg);
            tp.ResetState();
            float maxStereoDiff = 0f;
            for (int k = 0; k < 16; k++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.25f * MathF.Sin(2f * MathF.PI * 330f * (k * frames + i) / sr);
                tp.Process(l, r, frames);
                for (int i = 0; i < frames; i++) maxStereoDiff = MathF.Max(maxStereoDiff, MathF.Abs(l[i] - r[i]));
            }
            Console.WriteLine($"[PASS] Pre-Cab Stereo Preservation (DC-2W -> Stereo JC-120 -> Stereo 2x12 Cab IR): max|L-R|={maxStereoDiff:F4}");
            if (maxStereoDiff < 0.005f) throw new Exception($"Stereo width collapsed through JC-120/Cab! max|L-R|={maxStereoDiff}");

            // C) Parallel RC Passive Crossfade Tone Stack (Big Muff Pi) + TS808 Active Shelf Tone
            double bmLowAtT1 = MeasurePedalFreqDb("fuzz_bigmuff", new float[] { 5f, 1f, 6f, 0, 0, 0, 0, 0 }, 180.0);
            double bmHiAtT1 = MeasurePedalFreqDb("fuzz_bigmuff", new float[] { 5f, 1f, 6f, 0, 0, 0, 0, 0 }, 2800.0);
            double bmLowAtT9 = MeasurePedalFreqDb("fuzz_bigmuff", new float[] { 5f, 9f, 6f, 0, 0, 0, 0, 0 }, 180.0);
            double bmHiAtT9 = MeasurePedalFreqDb("fuzz_bigmuff", new float[] { 5f, 9f, 6f, 0, 0, 0, 0, 0 }, 2800.0);
            double tsHiDelta = MeasurePedalFreqDb("od_ts808", new float[] { 4f, 9.5f, 6f, 0, 0, 0, 0, 0 }, 3000.0)
                             - MeasurePedalFreqDb("od_ts808", new float[] { 4f, 1.5f, 6f, 0, 0, 0, 0, 0 }, 3000.0);
            double bmTiltSpan = (bmHiAtT9 - bmLowAtT9) - (bmHiAtT1 - bmLowAtT1);
            Console.WriteLine($"[PASS] Parallel RC Crossfade (Big Muff tilt={bmTiltSpan:F1}dB) & TS808 Active Tone (d3kHz={tsHiDelta:F1}dB)");
            if (bmTiltSpan < 8.0 || tsHiDelta < 6.0)
                throw new Exception($"Passive crossfade or TS808 active tone failed: bmTilt={bmTiltSpan:F1}, tsHi={tsHiDelta:F1}");

            // D) 7 Embedded Real-Mic Measured Cabinet IRs Verification & Acoustic Distinctness
            foreach (int slotIdx in new[] { 0, 2, 3, 4, 5, 6, 7 })
            {
                if (!tp.Chain.IsMeasuredCabIr(slotIdx))
                    throw new Exception($"Cab IR slot {slotIdx} did not load embedded measured WAV resource!");
            }
            double jcLo = MeasurePedalFreqDb("cab_jc120", new float[] { 20f, 16000f, 0f, 0, 0, 0, 0, 0 }, 100.0);
            double jcHi = MeasurePedalFreqDb("cab_jc120", new float[] { 20f, 16000f, 0f, 0, 0, 0, 0, 0 }, 4500.0);
            double mesaLo = MeasurePedalFreqDb("cab_mesa412", new float[] { 20f, 16000f, 0f, 0, 0, 0, 0, 0 }, 100.0);
            double mesaHi = MeasurePedalFreqDb("cab_mesa412", new float[] { 20f, 16000f, 0f, 0, 0, 0, 0, 0 }, 4500.0);
            double jensenLo = MeasurePedalFreqDb("cab_jensen112", new float[] { 20f, 16000f, 0f, 0, 0, 0, 0, 0 }, 100.0);
            double jcTilt = jcHi - jcLo;
            double mesaTilt = mesaHi - mesaLo;
            Console.WriteLine($"[PASS] 7 Embedded Real-Mic Measured Cab IRs (1024-tap): JC120 vs Mesa tilt={jcTilt - mesaTilt:+0.0;-0.0}dB, Mesa vs 1x12Jensen @100Hz={mesaLo - jensenLo:+0.0;-0.0}dB");
            if (jcTilt - mesaTilt < 10.0 || mesaLo - jensenLo < 4.0)
                throw new Exception("Embedded real-mic Cabinet IR bank failed acoustic distinctness check!");

            // E) Public Schematic Wah (Cry Baby GCB-95) & Ge Fuzz Face / Rangemaster 2nd Harmonic Physics
            double wahToe = MeasurePedalFreqDb("eq_crybaby", new float[] { 85f, 0f, 6f, 100f, 0, 0, 0, 0 }, 1600.0);
            double wahHeel = MeasurePedalFreqDb("eq_crybaby", new float[] { 15f, 0f, 6f, 100f, 0, 0, 0, 0 }, 1600.0);
            var (ffH2, _) = MeasurePedalHarmonics("fuzz_face", new float[] { 7.5f, 5f, 5.5f, 0f, 0, 0, 0, 0 });
            var (rmH2, _) = MeasurePedalHarmonics("boost_rangemaster", new float[] { 7f, 1f, 0f, 0, 0, 0, 0, 0 });
            Console.WriteLine($"[PASS] Cry Baby Wah 1.6kHz Toe-Heel sweep={wahToe - wahHeel:+0.0;-0.0}dB, FuzzFace 2ndHarm={ffH2:F1}%, Rangemaster 2ndHarm={rmH2:F1}%");
            if (wahToe - wahHeel < 6.0 || ffH2 < 5.0 || rmH2 < 3.0)
                throw new Exception($"Cry Baby or Ge schematic physics failed: wah={wahToe - wahHeel:F1}dB, ffH2={ffH2:F1}%, rmH2={rmH2:F1}%");

            // F) Audit Regression Checks: Klon 1kHz Mid Filter, Plexi Bright Toggle, Wah MIX=0% Dry Bypass
            double MeasureSmallSigFreqDb(string modelId, float[] p, double freqHz, float amp)
            {
                tp.Chain.UpdateChain(MakeSinglePedalChain(modelId, p));
                tp.ResetState();
                var sigL = new float[2048];
                var sigR = new float[2048];
                for (int i = 0; i < 2048; i++) sigL[i] = sigR[i] = amp * MathF.Sin(2f * MathF.PI * (float)freqHz * i / sr);
                for (int off = 0; off < 2048; off += 256)
                    tp.Process(sigL.AsSpan(off, 256), sigR.AsSpan(off, 256), 256);
                return 20.0 * Math.Log10(Math.Max(1e-9, Rms(sigL.AsSpan(512))));
            }
            double klonHi = MeasurePedalFreqDb("od_klon", new float[] { 9f, 5f, 5f, 0, 0, 0, 0, 0 }, 1000.0);
            double klonLo = MeasurePedalFreqDb("od_klon", new float[] { 1f, 5f, 5f, 0, 0, 0, 0, 0 }, 1000.0);
            double plexiBrOn = MeasureSmallSigFreqDb("amp_plexi", new float[] { 0.5f, 5f, 5f, 5f, 2.0f, 1f, 5f, 1f }, 3500.0, 0.02f);
            double plexiBrOff = MeasureSmallSigFreqDb("amp_plexi", new float[] { 0.5f, 5f, 5f, 5f, 2.0f, 1f, 5f, 0f }, 3500.0, 0.02f);
            double wahDryToe = MeasurePedalFreqDb("eq_crybaby", new float[] { 85f, 0f, 6f, 0f, 0, 0, 0, 0 }, 1600.0);
            double wahDryHeel = MeasurePedalFreqDb("eq_crybaby", new float[] { 15f, 0f, 6f, 0f, 0, 0, 0, 0 }, 1600.0);
            double wahDryDiff = Math.Abs(wahDryToe - wahDryHeel);
            Console.WriteLine($"[PASS] Audit Regressions: Klon1k={klonHi - klonLo:+0.0;-0.0}dB, PlexiBright={plexiBrOn - plexiBrOff:+0.0;-0.0}dB, WahMix0%Diff={wahDryDiff:F3}dB");
            if (klonHi <= klonLo + 2.0 || plexiBrOn - plexiBrOff < 1.5 || wahDryDiff >= 0.1)
                throw new Exception($"Regression check failed: klon={klonHi - klonLo:F2}, bright={plexiBrOn - plexiBrOff:F2}, wahDry={wahDryDiff:F3}");

            // G) Antigravity AI Tone Architect Verification (16 slots + 8 scenes + RT alloc: 0 B)
            var architect = new NeuralRigStudio.Services.AntigravityToneArchitect();
            string[] tonePrompts =
            {
                "보치더락 결속밴드 스타일 J-Rock 크런치 리드",
                "지미 헨드릭스 퍼즈페이스와 유니바이브 크라이베이비 와우",
                "존 메이어 블루스브레이커와 오렌지 스퀴저 클린",
            };
            SignalChainConfigDto? lastGen = null;
            foreach (var pr in tonePrompts)
            {
                var tr = architect.GenerateViaBuiltinArchitect(pr, null);
                if (!tr.Ok || tr.Chain.Slots.Count != 16 || tr.Chain.Scenes is not { Count: 8 })
                    throw new Exception($"Invalid ToneArchitect structure for '{pr}'");
                lastGen = tr.Chain;
                tp.Chain.UpdateChain(tr.Chain);
                tp.ResetState();
                for (int w = 0; w < 4; w++)
                {
                    for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
                    tp.Process(l, r, frames);
                }
                long gAlloc0 = GC.GetAllocatedBytesForCurrentThread();
                for (int k = 0; k < 20; k++)
                {
                    for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
                    tp.Process(l, r, frames);
                }
                long gAlloc = GC.GetAllocatedBytesForCurrentThread() - gAlloc0;
                if (gAlloc != 0 || !double.IsFinite(Rms(l)))
                    throw new Exception($"ToneArchitect chain failed RT audit for '{pr}': alloc={gAlloc}");
            }
            var modRes = architect.GenerateViaBuiltinArchitect("현재 톤에서 게인 더 올리고 블루스카이 쉬머 리버브 추가해줘", lastGen);
            if (!modRes.Ok || !modRes.Chain.Slots.Any(s => s.ModelId == "rev_bluesky"))
                throw new Exception("Relative AI tone edit did not inject rev_bluesky!");
            tp.Chain.UpdateChain(modRes.Chain);
            tp.ResetState();
            for (int w = 0; w < 4; w++) { for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i); tp.Process(l, r, frames); }
            long modAlloc0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++) { for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i); tp.Process(l, r, frames); }
            long modAlloc = GC.GetAllocatedBytesForCurrentThread() - modAlloc0;
            if (modAlloc != 0 || !double.IsFinite(Rms(l)))
                throw new Exception($"Relative AI edit allocated {modAlloc} B on RT path!");
            Console.WriteLine($"[PASS] Antigravity AI Tone Architect (4 prompts incl. relative edit): 16 slots + 8 scenes valid, RT alloc: {modAlloc} B");

            // H) Guided Guitar Analyzer Verification (Single-Coil vs Humbucker + RT zero-alloc confirmation)
            VerifyGuitarAnalyzerBench(tp, l, r, frames);
        }
    }

    static void VerifyGuitarAnalyzerBench(ToneProcessor tp, float[] l, float[] r, int frames)
    {
        const int sr = 44100;

        static GuitarProfileDto RunSyntheticProfile(string label, bool isSingleCoil)
        {
            var ga = new GuitarAnalyzer();
            ga.Start(label);
            var rng = new Random(isSingleCoil ? 101 : 202);
            var chunk = new float[512];

            void FeedBuf(Func<double, float> gen, double durationSec)
            {
                int total = (int)(durationSec * sr);
                int idx = 0;
                while (idx < total)
                {
                    int n = Math.Min(chunk.Length, total - idx);
                    for (int i = 0; i < n; i++)
                    {
                        double t = (idx + i) / (double)sr;
                        float noiseAmp = isSingleCoil ? 0.00025f : 0.00010f;
                        float noise = (float)((rng.NextDouble() * 2.0 - 1.0) * noiseAmp);
                        float hum = isSingleCoil
                            ? 0.0045f * MathF.Sin(2f * MathF.PI * 60f * (float)t)
                              + 0.0014f * MathF.Sin(2f * MathF.PI * 120f * (float)t)
                            : 0f;
                        chunk[i] = Math.Clamp(gen(t) + noise + hum, -0.99f, 0.99f);
                    }
                    ga.FeedDrySamples(chunk.AsSpan(0, n), sr);
                    idx += n;
                }
            }

            // Step 0: Silence / noise & hum floor (1.6s)
            FeedBuf(_ => 0f, 1.6);

            // Steps 1..4: E2, D3, B3, E4
            float[] notes = { 82.41f, 146.83f, 246.94f, 329.63f };
            float baseAmp = isSingleCoil ? 0.068f : 0.30f;
            float decayRate = isSingleCoil ? 1.45f : 0.80f;

            foreach (float f0 in notes)
            {
                FeedBuf(t =>
                {
                    float env = MathF.Exp(-decayRate * (float)t);
                    double w = 2.0 * Math.PI * f0 * t;
                    float s = isSingleCoil
                        ? 0.55f * (float)Math.Sin(w) + 0.42f * (float)Math.Sin(2 * w) + 0.36f * (float)Math.Sin(3 * w)
                          + 0.30f * (float)Math.Sin(4 * w) + 0.25f * (float)Math.Sin(6 * w) + 0.22f * (float)Math.Sin(9 * w)
                          + 0.16f * (float)Math.Sin(12 * w)
                        : 1.00f * (float)Math.Sin(w) + 0.32f * (float)Math.Sin(2 * w) + 0.14f * (float)Math.Sin(3 * w)
                          + 0.06f * (float)Math.Sin(4 * w);
                    return baseAmp * env * s;
                }, 1.35);

                // String mute pause between notes (> cooldown)
                FeedBuf(_ => 0f, 0.36);
            }

            // Step 5: Em Open Strum
            float[] chord = { 82.41f, 123.47f, 164.81f, 196.00f, 246.94f, 329.63f };
            FeedBuf(t =>
            {
                float env = MathF.Exp(-1.1f * (float)t);
                double sum = 0;
                for (int k = 0; k < chord.Length; k++)
                {
                    double w = 2.0 * Math.PI * chord[k] * t;
                    sum += isSingleCoil
                        ? 0.32 * Math.Sin(w) + 0.30 * Math.Sin(2 * w) + 0.28 * Math.Sin(4 * w)
                          + 0.26 * Math.Sin(7 * w) + 0.22 * Math.Sin(11 * w)
                        : 0.72 * Math.Sin(w) + 0.24 * Math.Sin(2 * w) + 0.10 * Math.Sin(3 * w);
                }
                float scale = isSingleCoil ? 0.028f : 0.115f;
                return scale * env * (float)sum;
            }, 1.25);

            var live = ga.GetLiveState();
            if (!live.Completed || live.DraftProfile is null)
                throw new Exception($"GuitarAnalyzer did not complete for '{label}' (step={live.StepIndex}, pct={live.ProgressPct})");
            return live.DraftProfile;
        }

        var sc = RunSyntheticProfile("Test Strat SC", isSingleCoil: true);
        var hb = RunSyntheticProfile("Test LesPaul HB", isSingleCoil: false);

        if (sc.Notes.Count != 4 || hb.Notes.Count != 4 || sc.Notes.Any(n => n.Skipped) || hb.Notes.Any(n => n.Skipped))
            throw new Exception("GuitarAnalyzer skipped note unexpectedly on synthetic clean signals!");
        foreach (var n in sc.Notes.Concat(hb.Notes))
        {
            if (MathF.Abs(n.CentsOffset) > 15f)
                throw new Exception($"Note {n.TargetNote} cents error too high: {n.CentsOffset}");
        }

        if (!sc.EstimatedPickupType.Contains("Single-Coil", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Expected Single-Coil classification, got '{sc.EstimatedPickupType}' (hum={sc.Hum60HzDb}, bright={sc.BrightnessScore}, out={sc.OutputPowerScore})");
        if (!hb.EstimatedPickupType.Contains("Humbucker", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Expected Humbucker classification, got '{hb.EstimatedPickupType}' (hum={hb.Hum60HzDb}, bright={hb.BrightnessScore}, out={hb.OutputPowerScore})");
        if (!(sc.Hum60HzDb > hb.Hum60HzDb + 10f && sc.BrightnessScore > hb.BrightnessScore && hb.OutputPowerScore > sc.OutputPowerScore))
            throw new Exception($"Separation check failed: SC(hum={sc.Hum60HzDb},br={sc.BrightnessScore},out={sc.OutputPowerScore}) vs HB(hum={hb.Hum60HzDb},br={hb.BrightnessScore},out={hb.OutputPowerScore})");

        // Also confirm ToneProcessor RT loop remains 0 B alloc after applying calibrated Trim & Gate
        tp.GateThresholdDb = sc.SuggestedGateDb;
        tp.OutputGainDb = sc.SuggestedInputTrimDb;
        long rt0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 20; k++) { for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(0.05f * i); tp.Process(l, r, frames); }
        long rtAlloc = GC.GetAllocatedBytesForCurrentThread() - rt0;
        if (rtAlloc != 0) throw new Exception($"Post-calibration RT alloc {rtAlloc} B != 0");

        // Quiet GP-100 SSS Stratocaster Stress Test:
        // -53 dBFS dry notes, missing E2 fundamental (H1=0.15 < H2=1.0), strong -44 dBFS 60Hz hum, Eb (-100ct) tuning, quiet strum
        {
            var gaQuiet = new GuitarAnalyzer();
            gaQuiet.Start("Quiet SSS Eb");
            var rngQ = new Random(303);
            var cBuf = new float[512];

            void FeedQuiet(Func<double, float> gen, double durationSec)
            {
                int total = (int)(durationSec * sr);
                int idx = 0;
                while (idx < total)
                {
                    int n = Math.Min(cBuf.Length, total - idx);
                    for (int i = 0; i < n; i++)
                    {
                        double t = (idx + i) / (double)sr;
                        float noise = (float)((rngQ.NextDouble() * 2.0 - 1.0) * 0.00018f);
                        float hum = 0.0089f * MathF.Sin(2f * MathF.PI * 60f * (float)t)
                                  + 0.0022f * MathF.Sin(2f * MathF.PI * 120f * (float)t); // -44 dBFS 60Hz hum
                        cBuf[i] = Math.Clamp(gen(t) + noise + hum, -0.99f, 0.99f);
                    }
                    gaQuiet.FeedDrySamples(cBuf.AsSpan(0, n), sr);
                    idx += n;
                }
            }

            FeedQuiet(_ => 0f, 1.3); // Silence
            float[] ebNotes = { 77.78f, 138.59f, 233.08f, 311.13f }; // Eb half-step down (-100 ct)
            foreach (float f0 in ebNotes)
            {
                FeedQuiet(t =>
                {
                    float env = MathF.Exp(-1.6f * (float)t);
                    double w = 2.0 * Math.PI * f0 * t;
                    // Weak fundamental (0.15) with dominant 2nd/3rd harmonics (~ -53 dBFS RMS)
                    float sig = 0.15f * (float)Math.Sin(w) + 1.00f * (float)Math.Sin(2 * w)
                              + 0.85f * (float)Math.Sin(3 * w) + 0.60f * (float)Math.Sin(4 * w);
                    return 0.0038f * env * sig;
                }, 1.05);
                FeedQuiet(_ => 0f, 0.30);
            }
            float[] ebChord = { 77.78f, 116.54f, 155.56f, 185.00f, 233.08f, 311.13f };
            FeedQuiet(t =>
            {
                float env = MathF.Exp(-1.3f * (float)t);
                double sum = 0;
                for (int k = 0; k < ebChord.Length; k++)
                {
                    double w = 2.0 * Math.PI * ebChord[k] * t;
                    sum += 0.35 * Math.Sin(w) + 0.35 * Math.Sin(2 * w) + 0.28 * Math.Sin(3 * w);
                }
                return 0.0028f * env * (float)sum; // ~ -38 dBFS strum
            }, 1.05);

            var qlive = gaQuiet.GetLiveState();
            if (!qlive.Completed || qlive.DraftProfile is null)
                throw new Exception($"Quiet Strat stress test did not complete! step={qlive.StepIndex} hint={qlive.StatusHint}");
            if (qlive.DraftProfile.Notes.Count != 4 || qlive.DraftProfile.Notes.Any(n => n.Skipped))
                throw new Exception("Quiet Strat stress test skipped a note!");
            foreach (var n in qlive.DraftProfile.Notes)
            {
                if (MathF.Abs(n.CentsOffset - (-100f)) > 25f)
                    throw new Exception($"Quiet Strat Eb tuning note {n.TargetNote} cents error: {n.CentsOffset}");
            }
        }

        // Wrong-Note Rejection Test: playing A2 (110 Hz) during E2 (82.4 Hz) must NOT unlock or advance
        {
            var gaReject = new GuitarAnalyzer();
            gaReject.Start("WrongNoteCheck");
            var cBuf = new float[512];
            for (int k = 0; k < 105; k++)
            {
                Array.Clear(cBuf);
                gaReject.FeedDrySamples(cBuf, sr);
            }
            if (gaReject.GetLiveState().StepIndex != 1)
                throw new Exception($"WrongNoteCheck expected StepIndex=1 after silence, got {gaReject.GetLiveState().StepIndex}");

            int samplePos = 0;
            for (int k = 0; k < 110; k++) // ~1.28s of wrong A2 (110 Hz) note
            {
                for (int i = 0; i < cBuf.Length; i++, samplePos++)
                    cBuf[i] = 0.08f * MathF.Sin(2f * MathF.PI * 110.0f * samplePos / sr);
                gaReject.FeedDrySamples(cBuf, sr);
            }
            if (gaReject.GetLiveState().StepIndex != 1)
                throw new Exception($"GuitarAnalyzer falsely advanced on wrong note A2! StepIndex={gaReject.GetLiveState().StepIndex}");
        }

        // Studio Chromatic MPM/NSDF Tuner Verification across all 6 strings (weak fundamental + quiet -52 dBFS + 60Hz hum + A4=432Hz)
        {
            var det = new GuitarPitchDetector();
            var win = new float[GuitarPitchDetector.WindowSize];
            (string expNote, int expOct, float f0)[] sixStrings =
            {
                ("E", 2, 82.41f),
                ("A", 2, 110.00f),
                ("D", 3, 146.83f),
                ("G", 3, 196.00f),
                ("B", 3, 246.94f),
                ("E", 4, 329.63f),
            };

            foreach (var (expNote, expOct, f0) in sixStrings)
            {
                det.Reset();
                for (int i = 0; i < win.Length; i++)
                {
                    double t = i / (double)sr;
                    double w = 2.0 * Math.PI * f0 * t;
                    // Weak fundamental (0.20) with stronger 2nd/3rd/4th harmonics at quiet -52 dBFS
                    float sig = 0.20f * (float)Math.Sin(w) + 1.00f * (float)Math.Sin(2 * w)
                              + 0.85f * (float)Math.Sin(3 * w) + 0.60f * (float)Math.Sin(4 * w);
                    win[i] = 0.0035f * sig;
                }
                PitchDetectionResult detRes = default;
                for (int f = 0; f < 3; f++)
                    detRes = det.Detect(win, sr, 440f, 62f);

                if (!detRes.Active || detRes.NoteName != expNote || detRes.Octave != expOct || MathF.Abs(detRes.Cents) > 3.0f)
                    throw new Exception($"MPM Tuner 6-string check failed for {expNote}{expOct} ({f0}Hz): got {detRes.NoteName}{detRes.Octave} ({detRes.FreqHz}Hz, {detRes.Cents}ct, active={detRes.Active})");
            }

            det.Reset();
            for (int i = 0; i < win.Length; i++)
                win[i] = 0.05f * MathF.Sin(2f * MathF.PI * 432.0f * i / sr);
            var r432 = det.Detect(win, sr, 432f, 62f);
            if (!r432.Active || r432.NoteName != "A" || r432.Octave != 4 || MathF.Abs(r432.Cents) > 1.0f)
                throw new Exception($"MPM Tuner A4=432Hz check failed: got {r432.NoteName}{r432.Octave} ({r432.Cents}ct)");
            Console.WriteLine("[PASS] Studio MPM/NSDF Chromatic Tuner: 6 strings (E2..E4 weak-fundamental @ -52dBFS) locked within +/-3ct, A4=432Hz verified");
        }

        Console.WriteLine($"[PASS] Guided Guitar Analyzer (+Quiet -53dBFS/Hum/Eb & Wrong-Note Rejection): SC='{sc.EstimatedPickupType}'(Br={sc.BrightnessScore},Out={sc.OutputPowerScore},Hum={sc.Hum60HzDb:F1}dB,Trim={sc.SuggestedInputTrimDb:+0.0;-0.0}dB) vs HB='{hb.EstimatedPickupType}'(Br={hb.BrightnessScore},Out={hb.OutputPowerScore},Hum={hb.Hum60HzDb:F1}dB,Trim={hb.SuggestedInputTrimDb:+0.0;-0.0}dB), RT alloc: {rtAlloc} B");
    }
}
