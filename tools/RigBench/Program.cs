using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using NAudio.Wave;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Plugin;
using NeuralRigStudio.Services;

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

    // 4.1) Zero-Allocation 44.1kHz -> 48kHz Real-Time Sample Rate Bridge (Interface -> PC WASAPI)
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
    Console.WriteLine($"[PASS] 44.1kHz->48kHz Zero-Alloc Sample-Rate Bridge (44.1k Interface -> 48k WASAPI, peak={bridgePeak:F3}, RT alloc: {bridgeAlloc} B)");
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

// 8) Hardware Similarity + Latency/RT + 16-Slot Multi-Chain Benchmark Suite
{
    var bench = RigBenchmarkSuite.Run(48000);
    foreach (var sim in bench.SimilarityItems)
    {
        Console.WriteLine($"  [SIM] {sim.OriginalHardware}: {sim.SimilarityPct:F1}% ({sim.MeasuredValue}) -> {(sim.Passed ? "PASS" : "FAIL")}");
    }
    foreach (var lat in bench.LatencyItems.Where(x => x.BufferFrames == 64))
    {
        Console.WriteLine($"  [64fr] {lat.ProfileName}: avg={lat.AvgBlockMs:F4}ms / {lat.BudgetMs:F3}ms ({lat.CpuLoadPct:F1}% CPU, RT={lat.RtAllocBytes}B) -> {(lat.Passed ? "PASS" : "FAIL")}");
    }
    foreach (var mc in bench.MultiChainItems)
    {
        Console.WriteLine($"  [CHAIN] {mc.ScenarioName}: avg={mc.AvgBlockMs:F4}ms ({mc.Detail}, RT={mc.RtAllocBytes}B) -> {(mc.Passed ? "PASS" : "FAIL")}");
    }
    if (!bench.OverallPassed || !bench.ZeroRtAllocAll)
        throw new Exception($"RigBenchmarkSuite failed: overall={bench.OverallPassed}, zeroAlloc={bench.ZeroRtAllocAll}, min64={bench.MinBuffer64Pass}");

    Console.WriteLine($"[PASS] RigBenchmarkSuite (AvgSimilarity={bench.OverallSimilarityPct:F1}%, 64fr={(bench.MinBuffer64Pass ? "PASS" : "FAIL")}, RT alloc: 0 B, Total={bench.TotalDurationMs:F0} ms)");
}

// 9) Platform Readiness Verification (PII LogSanitizer + OTA Choice/Force Policy + Community SPICE Safety)
{
    string noisy = @"Error in C:\Users\alice\Documents\secret.nam Authorization: Bearer eyJhbGciOi.payload.sig api_key=sk-123456";
    string scrubbed = LogSanitizer.Scrub(noisy);
    if (scrubbed.Contains("alice", StringComparison.OrdinalIgnoreCase) ||
        scrubbed.Contains("eyJhbGciOi", StringComparison.Ordinal) ||
        scrubbed.Contains("sk-123456", StringComparison.Ordinal))
        throw new Exception($"LogSanitizer failed to scrub PII/secrets: {scrubbed}");

    LogSanitizer.RecordLog(@"Audio opened for C:\Users\alice\test.wav");
    if (!LogSanitizer.GetRecentLogsText(10).Contains("%USERPROFILE%", StringComparison.Ordinal))
        throw new Exception("LogSanitizer ring buffer failed to record scrubbed line.");

    var (availOpt, forceOpt) = OtaUpdateService.EvaluateReleasePolicy("3.0.0", "3.1.0", "3.0.0", forceFlag: false);
    var (availForceFlag, forceByFlag) = OtaUpdateService.EvaluateReleasePolicy("3.0.0", "3.1.0", "3.0.0", forceFlag: true);
    var (availBelowMin, forceByMin) = OtaUpdateService.EvaluateReleasePolicy("3.0.0", "3.2.0", "3.1.0", forceFlag: false);
    var (availUpToDate, _) = OtaUpdateService.EvaluateReleasePolicy("3.2.0", "3.2.0", "3.1.0", forceFlag: true);
    if (!availOpt || forceOpt || !availForceFlag || !forceByFlag || !availBelowMin || !forceByMin || availUpToDate)
        throw new Exception("OtaUpdateService policy evaluation failed.");

    var sampleSpec = new CustomDeviceSpecDto
    {
        Id = "custom_drive_verify_bench",
        Name = "Verify Drive",
        ShortName = "Verify",
        OriginalName = "Verify Drive",
        Category = "drive",
        SpiceCircuit = new NeuralRigStudio.Audio.Spice.SpiceCircuitDto
        {
            InputVoltageScale = 0.45f,
            Stages = new List<NeuralRigStudio.Audio.Spice.SpiceStageDto>
            {
                new()
                {
                    Name = "s1",
                    InputNode = "in",
                    OutputNode = "out",
                    InputSeriesResistance = 1000f,
                    Elements = new List<NeuralRigStudio.Audio.Spice.SpiceElementDto>
                    {
                        new() { Name = "D1", Type = "D", Nodes = new[] { "out", "gnd" }, Model = "1N914" },
                        new() { Name = "D2", Type = "D", Nodes = new[] { "gnd", "out" }, Model = "1N914" },
                        new() { Name = "R1", Type = "R", Nodes = new[] { "in", "out" }, ValueText = "4.7k" },
                        new() { Name = "R2", Type = "R", Nodes = new[] { "out", "gnd" }, ValueText = "100k" },
                    },
                },
            },
        },
    };
    CloudPlatformService.ValidateCustomDeviceSpecSafety(sampleSpec);

    static string MakeTestJwt(long expUnix)
    {
        static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}.{B64Url($"{{\"exp\":{expUnix}}}")}.sig";
    }

    var nowRef = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);
    string expiredJwt = MakeTestJwt(nowRef.ToUnixTimeSeconds() - 60);
    string validJwt = MakeTestJwt(nowRef.ToUnixTimeSeconds() + 3600);
    string soonJwt = MakeTestJwt(nowRef.ToUnixTimeSeconds() + 30);
    if (!CloudPlatformService.IsAccessTokenExpiringSoon(expiredJwt, TimeSpan.FromMinutes(2), nowRef) ||
        CloudPlatformService.IsAccessTokenExpiringSoon(validJwt, TimeSpan.FromMinutes(2), nowRef) ||
        !CloudPlatformService.IsAccessTokenExpiringSoon(soonJwt, TimeSpan.FromMinutes(2), nowRef) ||
        CloudPlatformService.IsAccessTokenExpiringSoon("not-a-jwt", TimeSpan.FromMinutes(2), nowRef))
        throw new Exception("CloudPlatformService.IsAccessTokenExpiringSoon check failed.");

    var promptToday = new DateOnly(2026, 11, 24);
    var pNow = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    var (gUnselected, sUnselected) = CloudPlatformService.EvaluatePrompts(new AppSettings(), false, "unselected", pNow, promptToday);
    var (gGuestNew, sGuestNew) = CloudPlatformService.EvaluatePrompts(new AppSettings { LastGuestLoginPromptDate = null }, false, "guest", pNow, promptToday);
    var (gGuestSameDay, sGuestSameDay) = CloudPlatformService.EvaluatePrompts(new AppSettings { LastGuestLoginPromptDate = "2026-11-24" }, false, "guest", pNow, promptToday);
    var (gGuestPrevDay, sGuestPrevDay) = CloudPlatformService.EvaluatePrompts(new AppSettings { LastGuestLoginPromptDate = "2026-11-23" }, false, "guest", pNow, promptToday);
    var (g6d, s6d) = CloudPlatformService.EvaluatePrompts(new AppSettings { FirstGoogleSignInUtc = pNow.AddDays(-7).AddHours(1).ToString("O") }, true, "google", pNow, promptToday);
    var (g7d, s7d) = CloudPlatformService.EvaluatePrompts(new AppSettings { FirstGoogleSignInUtc = pNow.AddDays(-7).ToString("O") }, true, "google", pNow, promptToday);
    var (gShown, sShown) = CloudPlatformService.EvaluatePrompts(new AppSettings { FirstGoogleSignInUtc = pNow.AddDays(-14).ToString("O"), HasShownSponsorPrompt = true }, true, "google", pNow, promptToday);
    var (gSignedOut14d, sSignedOut14d) = CloudPlatformService.EvaluatePrompts(new AppSettings { FirstGoogleSignInUtc = pNow.AddDays(-14).ToString("O"), LastGuestLoginPromptDate = "2026-11-24" }, false, "guest", pNow, promptToday);

    if (gUnselected || sUnselected ||
        !gGuestNew || sGuestNew ||
        gGuestSameDay || sGuestSameDay ||
        !gGuestPrevDay || sGuestPrevDay ||
        g6d || s6d ||
        g7d || !s7d ||
        gShown || sShown ||
        gSignedOut14d || sSignedOut14d)
        throw new Exception("CloudPlatformService.EvaluatePrompts regression check failed.");

    Console.WriteLine("[PASS] Platform Gate (LogSanitizer PII/Secret Scrub + OTA Choice/Force SemVer Policy + Community Hub SPICE Safety + JWT Expiry & Prompt Policy)");
}

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
            // PITCH (3)
            "pitch_capo", "pitch_whammy", "pitch_octaver",
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

        // Quiet Single-Coil SSS Stratocaster Stress Test:
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

        // v1.1 Verification: Blackman-Harris SincResampler + Guitar-Aware AI Architect + Tempo Sync 0-Alloc RT Check
        {
            var src96 = new float[4800];
            for (int i = 0; i < src96.Length; i++)
                src96[i] = 0.8f * MathF.Sin(2f * MathF.PI * 8123f * i / 96000f);
            var dst48 = SincResampler.Resample(src96, 96000, 48000);
            float sincPeak = 0f;
            for (int i = 120; i < dst48.Length - 120; i++)
                sincPeak = MathF.Max(sincPeak, MathF.Abs(dst48[i]));
            if (MathF.Abs(sincPeak - 0.8f) > 0.015f)
                throw new Exception($"SincResampler high-freq attenuation out of spec: peak={sincPeak}");

            var arch = new NeuralRigStudio.Services.AntigravityToneArchitect();
            var aiRes = arch.GenerateViaBuiltinArchitect("128bpm 점8분 앰비언트 워십", null, sc);
            if (MathF.Abs(aiRes.Chain.TempoBpm - 128f) > 0.1f)
                throw new Exception($"Expected TempoBpm=128, got {aiRes.Chain.TempoBpm}");
            if (MathF.Abs(aiRes.Chain.InputGainDb - MathF.Round(sc.SuggestedInputTrimDb, 1)) > 0.2f)
                throw new Exception($"Expected Guitar InputGainDb={sc.SuggestedInputTrimDb}, got {aiRes.Chain.InputGainDb}");
            if (!aiRes.Chain.Slots.Any(s => s.SyncDivision == 2))
                throw new Exception("Expected dotted-eighth (SyncDivision=2) delay slot!");

            var syncProc = new ToneProcessor { Mode = ToneMode.SignalChain };
            syncProc.Prepare(256, 48000);
            syncProc.Chain.UpdateChain(aiRes.Chain);
            var bufL = new float[256];
            var bufR = new float[256];
            for (int w = 0; w < 32; w++)
            {
                for (int i = 0; i < 256; i++) bufL[i] = bufR[i] = 0.15f * MathF.Sin(2f * MathF.PI * 330f * i / 48000f);
                syncProc.Process(bufL, bufR, 256); // Tier-0/1 JIT warmup
            }

            var fasterChain = aiRes.Chain.Clone();
            fasterChain.TempoBpm = 142f;
            syncProc.Chain.UpdateChain(fasterChain);

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int b = 0; b < 32; b++)
                syncProc.Process(bufL, bufR, 256);
            long syncAlloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            if (syncAlloc != 0)
                throw new Exception($"Tempo-synced SignalChain allocated {syncAlloc} B on RT thread!");

            Console.WriteLine($"[PASS] v1.1 SincResampler (8kHz peak={sincPeak:F3}) + Guitar-Aware AI + Tempo Sync (128->142 BPM, 1/8.) verified (RT alloc: {syncAlloc} B)");
        }

        // Universal Input Verification: WASAPI Float32 / PCM16 / PCM24 + Multi-Channel (Ch3) 0-Alloc Decode
        {
            const int nFrames = 128;
            var outL = new float[nFrames];
            var outR = new float[nFrames];

            var rawF32 = new byte[nFrames * 4 * sizeof(float)];
            var spanF32 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(rawF32.AsSpan());
            for (int i = 0; i < nFrames; i++)
            {
                spanF32[i * 4 + 0] = 0.05f;
                spanF32[i * 4 + 1] = -0.05f;
                spanF32[i * 4 + 2] = 0.625f; // Guitar plugged into Input 3
                spanF32[i * 4 + 3] = 0.0f;
            }
            AudioRouter.DecodeWasapiPacketToStereo(rawF32, NAudio.CoreAudioApi.AudioClientBufferFlags.None, 4, 16, true, 32, 2, 2, outL, outR);
            if (MathF.Abs(outL[10] - 0.625f) > 1e-6f || MathF.Abs(outR[10] - 0.625f) > 1e-6f)
                throw new Exception($"Universal Ch3 Float32 decode mismatch: L={outL[10]}, R={outR[10]}");

            var rawPcm16 = new byte[nFrames * 2 * sizeof(short)];
            var spanPcm16 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(rawPcm16.AsSpan());
            for (int i = 0; i < nFrames; i++)
            {
                spanPcm16[i * 2] = 16384;
                spanPcm16[i * 2 + 1] = -16384;
            }
            AudioRouter.DecodeWasapiPacketToStereo(rawPcm16, NAudio.CoreAudioApi.AudioClientBufferFlags.None, 2, 4, false, 16, 0, 1, outL, outR);
            if (MathF.Abs(outL[0] - 0.5f) > 1e-4f || MathF.Abs(outR[0] + 0.5f) > 1e-4f)
                throw new Exception("Universal PCM16 decode mismatch!");

            var rawPcm24 = new byte[nFrames * 6];
            for (int i = 0; i < nFrames; i++)
            {
                int b = i * 6;
                rawPcm24[b + 0] = 0x00; rawPcm24[b + 1] = 0x00; rawPcm24[b + 2] = 0x40;
                rawPcm24[b + 3] = 0x00; rawPcm24[b + 4] = 0x00; rawPcm24[b + 5] = 0xC0;
            }
            AudioRouter.DecodeWasapiPacketToStereo(rawPcm24, NAudio.CoreAudioApi.AudioClientBufferFlags.None, 2, 6, false, 24, 0, 1, outL, outR);
            if (MathF.Abs(outL[0] - 0.5f) > 1e-5f || MathF.Abs(outR[0] + 0.5f) > 1e-5f)
                throw new Exception($"Universal PCM24 decode mismatch: L={outL[0]}, R={outR[0]}");

            long uAlloc0 = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < 50; iter++)
            {
                AudioRouter.DecodeWasapiPacketToStereo(rawF32, NAudio.CoreAudioApi.AudioClientBufferFlags.None, 4, 16, true, 32, 2, 2, outL, outR);
                AudioRouter.DecodeWasapiPacketToStereo(rawPcm16, NAudio.CoreAudioApi.AudioClientBufferFlags.None, 2, 4, false, 16, 0, 1, outL, outR);
                AudioRouter.DecodeWasapiPacketToStereo(rawPcm24, NAudio.CoreAudioApi.AudioClientBufferFlags.None, 2, 6, false, 24, 0, 1, outL, outR);
                AudioRouter.DecodeWasapiPacketToStereo(rawPcm24, NAudio.CoreAudioApi.AudioClientBufferFlags.Silent, 2, 6, false, 24, 0, 1, outL, outR);
            }
            long uAlloc = GC.GetAllocatedBytesForCurrentThread() - uAlloc0;
            if (uAlloc != 0)
                throw new Exception($"Universal WASAPI decoder allocated {uAlloc} B on RT path!");

            Console.WriteLine($"[PASS] Universal Input Decoder (WASAPI Float32/PCM16/PCM24/Silent + 4ch Ch3 routing) verified (RT alloc: {uAlloc} B)");
        }

        // v1.2 Verification: LSTM + ConvNet NAM + 3-Slot Multi-NAM + 16k Partitioned FFT Convolver + Dual Virtual Miking
        VerifyMilestone2Bench();

        // v1.3 Verification: Polyphonic Pitch Shifter (Virtual Capo / Whammy / Octaver) + 4-Slot DSP Pool Expansion
        VerifyMilestone3Bench();

        // v1.5 Verification: BPM-Synced Stereo Looper + Dual-Stream Recorder + 1-Click Live DI Re-amping
        VerifyMilestone4Bench();

        // v2.0 Verification: .nrig Bundle + SHA-256 + ToneHub Vault + NativeAOT VST3/CLAP Automation & State
        VerifyMilestone5Bench();

        // MCP Web Schematic Discovery + Zero-Alloc Custom Pedal/Amp/Cab DSP Verification
        VerifyMcpSchematicAdditionBench();
    }

    static void VerifyMilestone2Bench()
    {
        const int sr = 48000;
        const int frames = 256;

        // 1) Official NAM LSTM (1 layer, hidden=16) compile + .nam export/import round-trip + 0-alloc RT bench
        ToneProfile lstmProf;
        {
            var lstmCfg = new LstmConfigDto { NumLayers = 1, InputSize = 1, HiddenSize = 16 };
            int h = lstmCfg.HiddenSize;
            int totalIn = lstmCfg.InputSize + h;
            int wCount = 4 * h * totalIn + 4 * h + h + h + h + 1;
            var w = new float[wCount];
            var rng = new Random(1201);
            for (int i = 0; i < w.Length; i++)
                w[i] = (float)((rng.NextDouble() * 2.0 - 1.0) * 0.18);

            var rawProf = new ToneProfile
            {
                Id = "bench-lstm",
                Name = "Bench LSTM 1x16",
                Version = 3,
                Architecture = "LSTM",
                SampleRate = sr,
                LstmConfig = lstmCfg,
                Weights = w,
            };
            rawProf.PrepareForRt();
            if (!rawProf.IsLstm)
                throw new Exception("Failed to compile LSTM NAM model!");

            string tmpNam = Path.Combine(Path.GetTempPath(), $"nrs_lstm_{Guid.NewGuid():N}.nam");
            ToneProfile? imported = null;
            try
            {
                ToneProfileStore.ExportNamFile(rawProf, tmpNam);
                imported = ToneProfileStore.ImportNamFile(tmpNam, "Bench LSTM Imported");
                if (!imported.IsLstm || !string.Equals(imported.Architecture, "LSTM", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("LSTM .nam round-trip failed!");
            }
            finally
            {
                if (imported != null) ToneProfileStore.Delete(imported);
                try { if (File.Exists(tmpNam)) File.Delete(tmpNam); } catch { }
            }
            lstmProf = rawProf;

            var pLstm = new ToneProcessor { ActiveProfile = lstmProf, Mode = ToneMode.FullRig, GateThresholdDb = -80f };
            pLstm.Prepare(frames, sr);
            var inB = new float[frames];
            var outB = new float[frames];
            for (int i = 0; i < frames; i++) inB[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
            for (int warm = 0; warm < 8; warm++) pLstm.ProcessWaveNetRaw(inB, outB);

            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 200; k++) pLstm.ProcessWaveNetRaw(inB, outB);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 200.0;
            double outRms = Rms(outB);
            if (alloc != 0 || ms >= 1.2 || !double.IsFinite(outRms) || outRms < 1e-4)
                throw new Exception($"LSTM NAM bench failed: alloc={alloc}, ms={ms:F4}, rms={outRms}");
            Console.WriteLine($"[PASS] Official NAM LSTM (1x16, round-trip, 256fr): {ms:F4} ms/block, rms={outRms:F4}, RT alloc: {alloc} B");
        }

        // 2) Official NAM ConvNet (8ch, dilations=[1,2,4], BatchNorm=true) compile + .nam round-trip + 0-alloc RT bench
        ToneProfile convProf;
        {
            var convCfg = new ConvNetConfigDto
            {
                Channels = 8,
                Dilations = new[] { 1, 2, 4 },
                BatchNorm = true,
                KernelSize = 2,
            };
            int ch = convCfg.Channels;
            int kSz = convCfg.KernelSize;
            int wCount = 0;
            for (int li = 0; li < convCfg.Dilations.Length; li++)
            {
                int inCh = li == 0 ? 1 : ch;
                wCount += ch * inCh * kSz; // Conv1d W
                wCount += 4 * ch + 1;      // BatchNorm: mean, var, gamma, beta, eps
            }
            wCount += ch + 1;              // Head W + B
            var w = new float[wCount];
            var rng = new Random(1202);
            int idx = 0;
            for (int li = 0; li < convCfg.Dilations.Length; li++)
            {
                int inCh = li == 0 ? 1 : ch;
                for (int i = 0; i < ch * inCh * kSz; i++)
                    w[idx++] = (float)((rng.NextDouble() * 2.0 - 1.0) * 0.25);
                for (int oc = 0; oc < ch; oc++) w[idx++] = 0.02f;  // running_mean
                for (int oc = 0; oc < ch; oc++) w[idx++] = 0.50f;  // running_var
                for (int oc = 0; oc < ch; oc++) w[idx++] = 1.10f;  // gamma
                for (int oc = 0; oc < ch; oc++) w[idx++] = 0.01f;  // beta
                w[idx++] = 1e-5f;                                  // eps
            }
            for (int oc = 0; oc < ch; oc++) w[idx++] = 0.35f;      // headW
            w[idx++] = 0.0f;                                       // headB

            var rawProf = new ToneProfile
            {
                Id = "bench-conv",
                Name = "Bench ConvNet 8ch BN",
                Version = 3,
                Architecture = "ConvNet",
                SampleRate = sr,
                ConvNetConfig = convCfg,
                Weights = w,
            };
            rawProf.PrepareForRt();
            if (!rawProf.IsConvNet)
                throw new Exception("Failed to compile ConvNet NAM model!");

            string tmpNam = Path.Combine(Path.GetTempPath(), $"nrs_conv_{Guid.NewGuid():N}.nam");
            ToneProfile? imported = null;
            try
            {
                ToneProfileStore.ExportNamFile(rawProf, tmpNam);
                imported = ToneProfileStore.ImportNamFile(tmpNam, "Bench ConvNet Imported");
                if (!imported.IsConvNet || !string.Equals(imported.Architecture, "ConvNet", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("ConvNet .nam round-trip failed!");
            }
            finally
            {
                if (imported != null) ToneProfileStore.Delete(imported);
                try { if (File.Exists(tmpNam)) File.Delete(tmpNam); } catch { }
            }
            convProf = rawProf;

            var pConv = new ToneProcessor { ActiveProfile = convProf, Mode = ToneMode.FullRig, GateThresholdDb = -80f };
            pConv.Prepare(frames, sr);
            var inB = new float[frames];
            var outB = new float[frames];
            for (int i = 0; i < frames; i++) inB[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
            for (int warm = 0; warm < 8; warm++) pConv.ProcessWaveNetRaw(inB, outB);

            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 200; k++) pConv.ProcessWaveNetRaw(inB, outB);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 200.0;
            double outRms = Rms(outB);
            if (alloc != 0 || ms >= 1.2 || !double.IsFinite(outRms) || outRms < 1e-4)
                throw new Exception($"ConvNet NAM bench failed: alloc={alloc}, ms={ms:F4}, rms={outRms}");
            Console.WriteLine($"[PASS] Official NAM ConvNet (8ch BN-folded, round-trip, 256fr): {ms:F4} ms/block, rms={outRms:F4}, RT alloc: {alloc} B");
        }

        // 3) Per-Slot Independent Multi-NAM (3 simultaneous slots: WaveNet + LSTM + ConvNet)
        {
            var wnCfg = MakeTestWaveNetConfig(new[] { (1, 12, 1, new[] { 1, 2, 4, 8, 16 }, true) }, out var wnW);
            var wnProf = new ToneProfile
            {
                Id = "bench-wn-slot",
                Name = "Bench WaveNet Slot",
                Version = 3,
                Architecture = "WaveNet",
                SampleRate = sr,
                WaveNetConfig = wnCfg,
                Weights = wnW,
            };
            wnProf.PrepareForRt();

            var multiProc = new ToneProcessor { Mode = ToneMode.SignalChain };
            multiProc.Prepare(frames, sr);
            multiProc.SetModelLibrary(new[] { wnProf, lstmProf, convProf });

            var chain = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                chain.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };

            chain.Slots[0] = new ChainSlotDto { Index = 0, ModelId = "nam_capture", NamModelId = wnProf.Id, Enabled = true, Params = new float[] { 0f, 0f, 100f, 0, 0, 0, 0, 0 } };
            chain.Slots[1] = new ChainSlotDto { Index = 1, ModelId = "nam_capture", NamModelId = lstmProf.Id, Enabled = true, Params = new float[] { 0f, 0f, 100f, 0, 0, 0, 0, 0 } };
            chain.Slots[2] = new ChainSlotDto { Index = 2, ModelId = "nam_capture", NamModelId = convProf.Id, Enabled = true, Params = new float[] { 0f, 0f, 100f, 0, 0, 0, 0, 0 } };
            multiProc.Chain.UpdateChain(chain);

            var l = new float[frames];
            var r = new float[frames];
            for (int w = 0; w < 6; w++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                multiProc.Process(l, r, frames);
            }

            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                multiProc.Process(l, r, frames);
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            double rms1 = Rms(l);

            // Swap Slot 1 from LSTM -> WaveNet and verify independent output change
            chain.Slots[1].NamModelId = wnProf.Id;
            multiProc.Chain.UpdateChain(chain);
            multiProc.ResetState();
            for (int w = 0; w < 26; w++)
            {
                for (int i = 0; i < frames; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                multiProc.Process(l, r, frames);
            }
            double rms2 = Rms(l);

            if (alloc != 0 || !double.IsFinite(rms1) || !double.IsFinite(rms2) || Math.Abs(rms1 - rms2) < 1e-4)
                throw new Exception($"Multi-NAM 3-slot check failed: alloc={alloc}, rms1={rms1}, rms2={rms2}");
            Console.WriteLine($"[PASS] Multi-NAM 3-Slot Independent Pool (WaveNet + LSTM + ConvNet): rms1={rms1:F4} vs rms2={rms2:F4}, RT alloc: {alloc} B");
        }

        // 4) Zero-Latency Partitioned FFT Convolver (16,384 taps) exact equivalence & perf bench
        {
            const int irLen = 16384;
            var ir = new float[irLen];
            ir[0] = 0.7f;
            for (int i = 1; i < irLen; i++)
                ir[i] = 0.08f * MathF.Exp(-i * 0.0005f) * MathF.Cos(0.11f * i);

            var partIr = PartitionedIr.Compile(ir);
            var conv = new StereoPartitionedConvolver();
            conv.Prepare(256);

            const int totalSamples = 1024;
            var src = new float[totalSamples];
            for (int i = 0; i < totalSamples; i++)
                src[i] = 0.35f * MathF.Sin(2f * MathF.PI * 240f * i / sr) + 0.15f * MathF.Cos(2f * MathF.PI * 1350f * i / sr);

            var outL = new float[totalSamples];
            var outR = new float[totalSamples];
            Array.Copy(src, outL, totalSamples);
            Array.Copy(src, outR, totalSamples);

            // Process across uneven buffer sizes (64, 128, 256, 64, 192, 64, 256)
            int[] chunkSizes = { 64, 128, 256, 64, 192, 64, 256 };
            int offset = 0;
            foreach (int cs in chunkSizes)
            {
                conv.ConvolveStereo(outL.AsSpan(offset, cs), outR.AsSpan(offset, cs), cs, partIr);
                offset += cs;
            }

            float maxDiff = 0f;
            for (int n = 0; n < totalSamples; n++)
            {
                double exact = 0.0;
                int kMax = Math.Min(n + 1, irLen);
                for (int k = 0; k < kMax; k++)
                    exact += (double)ir[k] * src[n - k];
                maxDiff = MathF.Max(maxDiff, MathF.Abs(outL[n] - (float)exact));
            }
            if (maxDiff > 1e-4f)
                throw new Exception($"PartitionedConvolver 16k numerical mismatch: maxDiff={maxDiff}");

            // Perf & 0-alloc bench on 256fr stereo
            var pL = new float[256];
            var pR = new float[256];
            for (int w = 0; w < 8; w++)
            {
                for (int i = 0; i < 256; i++) { pL[i] = 0.2f * MathF.Sin(0.05f * i); pR[i] = 0.2f * MathF.Cos(0.04f * i); }
                conv.ConvolveStereo(pL, pR, 256, partIr);
            }
            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 200; k++)
            {
                for (int i = 0; i < 256; i++) { pL[i] = 0.2f * MathF.Sin(0.05f * i); pR[i] = 0.2f * MathF.Cos(0.04f * i); }
                conv.ConvolveStereo(pL, pR, 256, partIr);
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 200.0;
            if (alloc != 0 || ms >= 0.5)
                throw new Exception($"PartitionedConvolver 16k perf/alloc failed: alloc={alloc}, ms={ms:F4}");

            Console.WriteLine($"[PASS] Zero-Latency Partitioned FFT Convolver (16,384 taps, uneven 64/128/256fr): max_diff={maxDiff:E2}, {ms:F4} ms/block, RT alloc: {alloc} B");
        }

        // 5) Dual Virtual Miking DSP (Cap vs Edge tilt, Phase Cancellation, Stereo Haas Width & 0-Alloc)
        {
            var cabProc = new ToneProcessor { Mode = ToneMode.SignalChain };
            cabProc.Prepare(frames, sr);

            double MeasureCab5k(CabMicConfigDto micCfg)
            {
                var c = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
                for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                    c.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
                c.Slots[0] = new ChainSlotDto
                {
                    Index = 0,
                    ModelId = "cab_v30",
                    Enabled = true,
                    Params = new float[] { 20f, 16000f, 0f, 0, 0, 0, 0, 0 },
                    CabMic = micCfg,
                };
                cabProc.Chain.UpdateChain(c);
                cabProc.ResetState();
                var l = new float[2048];
                var r = new float[2048];
                for (int i = 0; i < 2048; i++) l[i] = r[i] = 0.2f * MathF.Sin(2f * MathF.PI * 5000f * i / sr);
                for (int off = 0; off < 2048; off += frames)
                    cabProc.Process(l.AsSpan(off, frames), r.AsSpan(off, frames), frames);
                return 20.0 * Math.Log10(Math.Max(1e-9, Rms(l.AsSpan(512))));
            }

            double capDb = MeasureCab5k(new CabMicConfigDto { DualEnabled = true, MicAType = 0, MicAX = 0f, MicADist = 0.1f, MicAPan = 0f, Blend = 0f });
            double edgeDb = MeasureCab5k(new CabMicConfigDto { DualEnabled = true, MicAType = 0, MicAX = 1f, MicADist = 0.1f, MicAPan = 0f, Blend = 0f });
            if (capDb - edgeDb < 2.0)
                throw new Exception($"Virtual Cab Room Cap vs Edge 5kHz tilt too small: {capDb - edgeDb:F2} dB");

            // Phase cancellation check (identical Mic A & B, MicBPhaseInv=true, Blend=0.5, Delay=0)
            var cancelChain = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                cancelChain.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            cancelChain.Slots[0] = new ChainSlotDto
            {
                Index = 0,
                ModelId = "cab_v30",
                Enabled = true,
                Params = new float[] { 20f, 16000f, 0f, 0, 0, 0, 0, 0 },
                CabMic = new CabMicConfigDto
                {
                    DualEnabled = true,
                    MicAType = 0, MicAX = 0.2f, MicADist = 0.2f, MicALevelDb = 0f, MicAPan = 0f, MicAPhaseInv = false,
                    MicBType = 0, MicBX = 0.2f, MicBDist = 0.2f, MicBLevelDb = 0f, MicBPan = 0f, MicBPhaseInv = true,
                    MicBDelayMs = 0f, Blend = 0.5f,
                },
            };
            cabProc.Chain.UpdateChain(cancelChain);
            cabProc.ResetState();
            float cancelPeak = 0f;
            var cl = new float[frames];
            var cr = new float[frames];
            for (int b = 0; b < 8; b++)
            {
                for (int i = 0; i < frames; i++) cl[i] = cr[i] = 0.25f * MathF.Sin(2f * MathF.PI * 440f * (b * frames + i) / sr);
                cabProc.Process(cl, cr, frames);
                for (int i = 0; i < frames; i++) cancelPeak = MathF.Max(cancelPeak, MathF.Max(MathF.Abs(cl[i]), MathF.Abs(cr[i])));
            }
            if (cancelPeak >= 1e-4f)
                throw new Exception($"Dual Mic phase cancellation failed: peak={cancelPeak}");

            // Haas Stereo Width + 0-alloc check
            cancelChain.Slots[0].CabMic = new CabMicConfigDto
            {
                DualEnabled = true,
                MicAType = 0, MicAX = 0.2f, MicADist = 0.15f, MicALevelDb = 0f, MicAPan = -1f, MicAPhaseInv = false,
                MicBType = 1, MicBX = 0.5f, MicBDist = 0.25f, MicBLevelDb = 0f, MicBPan = 1f, MicBPhaseInv = false,
                MicBDelayMs = 8.0f, Blend = 0.5f,
            };
            cabProc.Chain.UpdateChain(cancelChain);
            cabProc.ResetState();
            for (int w = 0; w < 6; w++)
            {
                for (int i = 0; i < frames; i++) cl[i] = cr[i] = 0.25f * MathF.Sin(2f * MathF.PI * 330f * (w * frames + i) / sr);
                cabProc.Process(cl, cr, frames);
            }
            float haasStereoDiff = 0f;
            long hAlloc0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++)
            {
                for (int i = 0; i < frames; i++) cl[i] = cr[i] = 0.25f * MathF.Sin(2f * MathF.PI * 330f * (k * frames + i) / sr);
                cabProc.Process(cl, cr, frames);
                for (int i = 0; i < frames; i++) haasStereoDiff = MathF.Max(haasStereoDiff, MathF.Abs(cl[i] - cr[i]));
            }
            long hAlloc = GC.GetAllocatedBytesForCurrentThread() - hAlloc0;
            if (hAlloc != 0 || haasStereoDiff < 0.05f)
                throw new Exception($"Dual Mic Haas stereo check failed: alloc={hAlloc}, max|L-R|={haasStereoDiff}");

            Console.WriteLine($"[PASS] Dual Virtual Miking (Cap-Edge 5kHz={capDb - edgeDb:+0.0;-0.0}dB, PhaseCancel={cancelPeak:E1}, Haas max|L-R|={haasStereoDiff:F3}, RT alloc: {hAlloc} B)");
        }
    }

    static void VerifyMilestone3Bench()
    {
        const int sr = 48000;
        const int frames = 256;

        static double Goertzel(ReadOnlySpan<float> x, double targetHz, int sampleRate)
        {
            double w = 2.0 * Math.PI * targetHz / sampleRate;
            double cosW = Math.Cos(w);
            double coeff = 2.0 * cosW;
            double s0 = 0, s1 = 0, s2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                s0 = x[i] + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            double mag2 = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            return Math.Sqrt(Math.Max(0.0, mag2)) / Math.Max(1, x.Length);
        }

        // 1) Virtual Capo Polyphonic Dyad Shift (220Hz + 330Hz, -2 semitones -> 196.0Hz + 293.7Hz)
        {
            var pitchProc = new ToneProcessor { Mode = ToneMode.SignalChain };
            pitchProc.Prepare(frames, sr);
            var cfg = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                cfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            cfg.Slots[0] = new ChainSlotDto
            {
                Index = 0,
                ModelId = "pitch_capo",
                Enabled = true,
                Params = new float[] { -2f, 0f, 100f, 0f, 0, 0, 0, 0 },
            };
            pitchProc.Chain.UpdateChain(cfg);

            const int blocks = 32;
            var fullOut = new float[blocks * frames];
            var bl = new float[frames];
            var br = new float[frames];
            long sampleIdx = 0;

            for (int b = 0; b < blocks; b++)
            {
                for (int i = 0; i < frames; i++, sampleIdx++)
                {
                    float s = 0.22f * MathF.Sin(2f * MathF.PI * 220.0f * sampleIdx / sr)
                            + 0.22f * MathF.Sin(2f * MathF.PI * 330.0f * sampleIdx / sr);
                    bl[i] = br[i] = s;
                }
                pitchProc.Process(bl, br, frames);
                Array.Copy(bl, 0, fullOut, b * frames, frames);
            }

            var steady = fullOut.AsSpan(16 * frames, 16 * frames);
            double e196 = Goertzel(steady, 196.0, sr);
            double e220 = Goertzel(steady, 220.0, sr);
            double e294 = Goertzel(steady, 293.66, sr);
            double e330 = Goertzel(steady, 330.0, sr);
            double db1 = 20.0 * Math.Log10(Math.Max(1e-9, e196) / Math.Max(1e-9, e220));
            double db2 = 20.0 * Math.Log10(Math.Max(1e-9, e294) / Math.Max(1e-9, e330));

            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 200; k++)
            {
                for (int i = 0; i < frames; i++) bl[i] = br[i] = 0.2f * MathF.Sin(0.05f * i);
                pitchProc.Process(bl, br, frames);
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 200.0;

            if (alloc != 0 || ms >= 1.2 || db1 < 10.0 || db2 < 10.0)
                throw new Exception($"Virtual Capo poly dyad check failed: alloc={alloc}, ms={ms:F4}, d196/220={db1:F1}dB, d294/330={db2:F1}dB");

            Console.WriteLine($"[PASS] Poly Virtual Capo (-2st Dyad 220+330Hz -> 196+294Hz): +{db1:F1}/+{db2:F1}dB sep, {ms:F4} ms/block, RT alloc: {alloc} B");
        }

        // 2) Poly Whammy Continuous Sweep (0% -> 100%, +12 st on 220Hz -> 440Hz)
        {
            var whProc = new ToneProcessor { Mode = ToneMode.SignalChain };
            whProc.Prepare(frames, sr);
            var cfg = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                cfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            cfg.Slots[0] = new ChainSlotDto
            {
                Index = 0,
                ModelId = "pitch_whammy",
                Enabled = true,
                Params = new float[] { 0f, 12f, 100f, 0f, 0, 0, 0, 0 },
            };

            const int totalBlocks = 40;
            var outBuf = new float[totalBlocks * frames];
            var bl = new float[frames];
            var br = new float[frames];
            long sampleIdx = 0;

            for (int b = 0; b < totalBlocks; b++)
            {
                float pedal = b < 12 ? (b / 11f) * 100f : 100f;
                cfg.Slots[0].Params[0] = pedal;
                whProc.Chain.UpdateChain(cfg);

                for (int i = 0; i < frames; i++, sampleIdx++)
                    bl[i] = br[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220.0f * sampleIdx / sr);

                long a0 = GC.GetAllocatedBytesForCurrentThread();
                whProc.Process(bl, br, frames);
                long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                if (alloc != 0) throw new Exception($"Poly Whammy allocated {alloc} B during sweep!");
                Array.Copy(bl, 0, outBuf, b * frames, frames);
            }

            var steady = outBuf.AsSpan(24 * frames, 16 * frames);
            double e440 = Goertzel(steady, 440.0, sr);
            double e220 = Goertzel(steady, 220.0, sr);
            double whammyDb = 20.0 * Math.Log10(Math.Max(1e-9, e440) / Math.Max(1e-9, e220));
            if (whammyDb < 10.0)
                throw new Exception($"Poly Whammy sweep (+12st) failed: 440/220={whammyDb:F1}dB");

            Console.WriteLine($"[PASS] Poly Whammy Continuous Sweep (0->100%, +12st): 440Hz vs 220Hz = +{whammyDb:F1}dB, RT alloc: 0 B");
        }

        // 3) Poly Octaver (-1 Oct 110Hz + Dry 220Hz + +1 Oct 440Hz simultaneous)
        {
            var octProc = new ToneProcessor { Mode = ToneMode.SignalChain };
            octProc.Prepare(frames, sr);
            var cfg = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                cfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            cfg.Slots[0] = new ChainSlotDto
            {
                Index = 0,
                ModelId = "pitch_octaver",
                Enabled = true,
                Params = new float[] { 80f, 80f, 80f, 8.0f, 0f, 0, 0, 0 },
            };
            octProc.Chain.UpdateChain(cfg);

            const int blocks = 32;
            var outBuf = new float[blocks * frames];
            var bl = new float[frames];
            var br = new float[frames];
            long sampleIdx = 0;

            for (int b = 0; b < blocks; b++)
            {
                for (int i = 0; i < frames; i++, sampleIdx++)
                    bl[i] = br[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220.0f * sampleIdx / sr);
                octProc.Process(bl, br, frames);
                Array.Copy(bl, 0, outBuf, b * frames, frames);
            }

            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 64; k++)
            {
                for (int i = 0; i < frames; i++, sampleIdx++)
                    bl[i] = br[i] = 0.25f * MathF.Sin(2f * MathF.PI * 220.0f * sampleIdx / sr);
                octProc.Process(bl, br, frames);
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            if (alloc != 0) throw new Exception($"Poly Octaver allocated {alloc} B!");

            var steady = outBuf.AsSpan(16 * frames, 16 * frames);
            double e110 = Goertzel(steady, 110.0, sr);
            double e220 = Goertzel(steady, 220.0, sr);
            double e440 = Goertzel(steady, 440.0, sr);
            double eNoise = Goertzel(steady, 315.0, sr);
            if (e110 < 0.02 || e220 < 0.02 || e440 < 0.02 || e110 <= eNoise * 5.0 || e440 <= eNoise * 5.0)
                throw new Exception($"Poly Octaver failed: e110={e110:F4}, e220={e220:F4}, e440={e440:F4}, noise={eNoise:F4}");

            Console.WriteLine($"[PASS] Poly Octaver (220Hz -> 110/220/440Hz): e110={e110:F3}, e220={e220:F3}, e440={e440:F3}, RT alloc: {alloc} B");
        }

        // 4) 4-Slot Expanded DSP Resource Pools (4 Amps + 4 Cabs + 4 Delays + 4 Reverbs simultaneously active = 16/16 slots)
        {
            var quadProc = new ToneProcessor { Mode = ToneMode.SignalChain };
            quadProc.Prepare(frames, sr);
            var cfg = new SignalChainConfigDto
            {
                RoutingMode = (int)ChainRoutingMode.ParallelSplit,
                SplitCol = 1,
                MergeCol = 5,
                ParallelMix = 0.5f,
            };
            string[] slots16 =
            {
                "amp_plexi", "cab_v30", "dly_analog", "rev_spring",
                "amp_2203", "cab_1960a", "dly_digital", "rev_hall",
                "amp_twin", "cab_jc120", "dly_dm2w", "rev_plate",
                "amp_recto", "cab_mesa412", "dly_timeline", "rev_bluesky",
            };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
            {
                var def = NeuralRigStudio.Services.AntigravityToneArchitect.Catalog[slots16[i]];
                var p = new float[8];
                Array.Copy(def.Defaults, p, def.Defaults.Length);
                cfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = slots16[i], Enabled = true, Params = p };
            }
            quadProc.Chain.UpdateChain(cfg);

            var bl = new float[frames];
            var br = new float[frames];
            for (int w = 0; w < 8; w++)
            {
                for (int i = 0; i < frames; i++) bl[i] = br[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                quadProc.Process(bl, br, frames);
            }

            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 100; k++)
            {
                for (int i = 0; i < frames; i++) bl[i] = br[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                quadProc.Process(bl, br, frames);
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 100.0;
            double outRms = Rms(bl);
            double budgetMs = frames * 1000.0 / sr;
            if (alloc != 0 || !double.IsFinite(outRms) || outRms < 1e-4 || ms >= budgetMs)
                throw new Exception($"4-Slot Pool 16-block test failed: alloc={alloc}, ms={ms:F4}, rms={outRms}");

            Console.WriteLine($"[PASS] 4-Slot DSP Pool Expansion (4xAmp + 4xCab + 4xDelay + 4xReverb active, 256fr): {ms:F4} ms/block (Budget: {budgetMs:F2} ms), RT alloc: {alloc} B");
        }
    }

    static void VerifyMilestone4Bench()
    {
        const int sr = 48000;
        const int frames = 256;

        static double Goertzel(ReadOnlySpan<float> x, double targetHz, int sampleRate)
        {
            double w = 2.0 * Math.PI * targetHz / sampleRate;
            double cosW = Math.Cos(w);
            double coeff = 2.0 * cosW;
            double s0 = 0, s1 = 0, s2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                s0 = x[i] + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            double mag2 = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            return Math.Sqrt(Math.Max(0.0, mag2)) / Math.Max(1, x.Length);
        }

        // 1) BPM-Synced Stereo Looper (Quantize Snap, 1-Level 0B Undo/Redo, Half-Speed 0.5x, Reverse, 0B RT Alloc)
        {
            var looper = new StereoLooper();
            looper.Prepare(sr, maxSeconds: 30);
            looper.QuantizeSync = true;
            looper.Level = 1.0f;

            var bl = new float[frames];
            var br = new float[frames];

            // Record ~0.96 beat @ 120 BPM (90 * 256 = 23,040 frames -> should quantize-snap to 1 beat = 24,000 frames)
            looper.TriggerRecordOrOverdub();
            long sampleIdx = 0;
            for (int b = 0; b < 90; b++)
            {
                for (int i = 0; i < frames; i++, sampleIdx++)
                {
                    float s = 0.30f * MathF.Sin(2f * MathF.PI * 220f * sampleIdx / sr);
                    bl[i] = s;
                    br[i] = -s;
                }
                looper.Process(bl, br, frames, 120f);
            }

            // Switch Recording -> Playing
            looper.TriggerRecordOrOverdub();
            Array.Clear(bl);
            Array.Clear(br);
            looper.Process(bl, br, frames, 120f);

            if (looper.State != LooperState.Playing || looper.LoopLengthFrames != 24000)
                throw new Exception($"Looper BPM quantize failed: state={looper.State}, len={looper.LoopLengthFrames} (expected 24000)");

            // Verify Base playback (silence input -> loop outputs 220Hz stereo inverted)
            var playBuf = new float[frames * 8];
            for (int b = 0; b < 8; b++)
            {
                Array.Clear(bl);
                Array.Clear(br);
                looper.Process(bl, br, frames, 120f);
                Array.Copy(bl, 0, playBuf, b * frames, frames);
            }
            double base220 = Goertzel(playBuf, 220.0, sr);
            if (base220 < 0.08)
                throw new Exception($"Looper base playback weak: e220={base220:F4}");

            // Overdub 440Hz across full 24,000-frame loop (~94 blocks * 256)
            looper.TriggerRecordOrOverdub();
            sampleIdx = 0;
            for (int b = 0; b < 96; b++)
            {
                for (int i = 0; i < frames; i++, sampleIdx++)
                {
                    float s = 0.25f * MathF.Sin(2f * MathF.PI * 440f * sampleIdx / sr);
                    bl[i] = br[i] = s;
                }
                looper.Process(bl, br, frames, 120f);
            }
            looper.TriggerRecordOrOverdub(); // Back to Playing

            // Verify Undo removes 440Hz while preserving 220Hz
            if (!looper.Undo() || looper.CanUndo || !looper.CanRedo)
                throw new Exception("Looper Undo state flags invalid!");
            // Advance 1 block for 64-sample click-free dub ramp to settle
            Array.Clear(bl);
            Array.Clear(br);
            looper.Process(bl, br, frames, 120f);

            var undoBuf = new float[frames * 8];
            for (int b = 0; b < 8; b++)
            {
                Array.Clear(bl);
                Array.Clear(br);
                looper.Process(bl, br, frames, 120f);
                Array.Copy(bl, 0, undoBuf, b * frames, frames);
            }
            double undo220 = Goertzel(undoBuf, 220.0, sr);
            double undo440 = Goertzel(undoBuf, 440.0, sr);
            if (undo220 < 0.08 || undo440 > 0.01)
                throw new Exception($"Looper Undo failed: e220={undo220:F4}, e440={undo440:F4}");

            // Verify Redo restores 440Hz
            if (!looper.Redo() || !looper.CanUndo || looper.CanRedo)
                throw new Exception("Looper Redo state flags invalid!");
            Array.Clear(bl);
            Array.Clear(br);
            looper.Process(bl, br, frames, 120f);

            var redoBuf = new float[frames * 8];
            for (int b = 0; b < 8; b++)
            {
                Array.Clear(bl);
                Array.Clear(br);
                looper.Process(bl, br, frames, 120f);
                Array.Copy(bl, 0, redoBuf, b * frames, frames);
            }
            double redo440 = Goertzel(redoBuf, 440.0, sr);
            if (redo440 < 0.06)
                throw new Exception($"Looper Redo restore failed: e440={redo440:F4}");

            // Half-Speed (0.5x) & Reverse + JIT warmup then 0B RT allocation bench
            looper.HalfSpeed = true;
            looper.Reverse = true;
            for (int w = 0; w < 16; w++)
            {
                Array.Clear(bl);
                Array.Clear(br);
                looper.Process(bl, br, frames, 120f);
            }
            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 100; k++)
            {
                Array.Clear(bl);
                Array.Clear(br);
                looper.Process(bl, br, frames, 120f);
            }
            long lpAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            sw.Stop();
            double lpMs = sw.Elapsed.TotalMilliseconds / 100.0;
            if (lpAlloc != 0 || lpMs >= 0.5)
                throw new Exception($"StereoLooper RT alloc/perf failed: alloc={lpAlloc} B, ms={lpMs:F4}");

            Console.WriteLine($"[PASS] BPM-Synced Stereo Looper (120BPM Snap={looper.LoopLengthFrames}fr, Undo e440={undo440:F4} -> Redo e440={redo440:F4}, 0.5x+Rev): {lpMs:F4} ms/block, RT alloc: {lpAlloc} B");
        }

        // 2) Dual-Stream Recorder (Dry DI + Stereo Wet WAV) & 1-Click Real-Time DI Re-amping (0B RT Alloc)
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), $"nrs_rec_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);
            try
            {
                var tone = new ToneProcessor { Mode = ToneMode.SignalChain };
                tone.Prepare(frames, sr);

                static SignalChainConfigDto BuildAmpChain(string ampId, string cabId, bool withOd)
                {
                    var cfg = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
                    for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                        cfg.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
                    int idx = 0;
                    if (withOd)
                    {
                        var od = NeuralRigStudio.Services.AntigravityToneArchitect.Catalog["od_ts808"];
                        var p = new float[8];
                        Array.Copy(od.Defaults, p, od.Defaults.Length);
                        p[0] = 8.5f;
                        cfg.Slots[idx++] = new ChainSlotDto { Index = idx - 1, ModelId = "od_ts808", Enabled = true, Params = p };
                    }
                    var amp = NeuralRigStudio.Services.AntigravityToneArchitect.Catalog[ampId];
                    var pa = new float[8];
                    Array.Copy(amp.Defaults, pa, amp.Defaults.Length);
                    if (ampId == "amp_recto" && pa.Length > 0) pa[0] = 8.5f;
                    cfg.Slots[idx++] = new ChainSlotDto { Index = idx - 1, ModelId = ampId, Enabled = true, Params = pa };

                    var cab = NeuralRigStudio.Services.AntigravityToneArchitect.Catalog[cabId];
                    var pc = new float[8];
                    Array.Copy(cab.Defaults, pc, cab.Defaults.Length);
                    cfg.Slots[idx++] = new ChainSlotDto { Index = idx - 1, ModelId = cabId, Enabled = true, Params = pc };
                    return cfg;
                }

                tone.Chain.UpdateChain(BuildAmpChain("amp_twin", "cab_v30", withOd: false));

                using var rec = new DualStreamRecorder();
                rec.Prepare(sr, ringSeconds: 4);

                var dl = new float[frames];
                var dr = new float[frames];

                // JIT warmup before starting timed recording / measuring RT alloc
                rec.StartRecording(tmpDir, "WarmTake");
                for (int w = 0; w < 8; w++)
                {
                    for (int i = 0; i < frames; i++) dl[i] = dr[i] = 0.25f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                    rec.InjectReampDryRt(dl, dr, frames);
                    rec.CaptureDryRt(dl, dr, frames);
                    tone.Process(dl, dr, frames);
                    rec.CaptureWetRt(dl, dr, frames);
                }
                rec.StopRecording();
                rec.SetReampState(enabled: true, playing: true, loop: true);
                for (int w = 0; w < 8; w++)
                {
                    Array.Clear(dl);
                    Array.Clear(dr);
                    rec.InjectReampDryRt(dl, dr, frames);
                }
                rec.SetReampState(enabled: false, playing: false);

                // Actual measured Dual-Stream Recording (64 blocks = 16,384 frames)
                rec.StartRecording(tmpDir, "BenchTake");
                long sIdx = 0;
                long rAlloc0 = GC.GetAllocatedBytesForCurrentThread();
                for (int b = 0; b < 64; b++)
                {
                    for (int i = 0; i < frames; i++, sIdx++)
                    {
                        float dry = 0.25f * MathF.Sin(2f * MathF.PI * 196f * sIdx / sr)
                                  + 0.08f * MathF.Sin(2f * MathF.PI * 392f * sIdx / sr);
                        dl[i] = dr[i] = dry;
                    }
                    rec.CaptureDryRt(dl, dr, frames);
                    tone.Process(dl, dr, frames);
                    rec.CaptureWetRt(dl, dr, frames);
                }
                long capAlloc = GC.GetAllocatedBytesForCurrentThread() - rAlloc0;
                var recSnap = rec.StopRecording();

                if (capAlloc != 0)
                    throw new Exception($"DualStreamRecorder capture allocated {capAlloc} B on RT thread!");
                if (!File.Exists(recSnap.LastDryPath) || !File.Exists(recSnap.LastWetPath))
                    throw new Exception($"Dual-Stream WAV files missing: dry={recSnap.LastDryPath}, wet={recSnap.LastWetPath}");

                static int CountWavFrames(string path)
                {
                    using var r = new AudioFileReader(path);
                    int ch = r.WaveFormat.Channels;
                    int total = 0;
                    var tmp = new float[2048 * ch];
                    int got;
                    while ((got = r.Read(tmp.AsSpan())) > 0) total += got / ch;
                    return total;
                }

                int dryFrames = CountWavFrames(recSnap.LastDryPath);
                int wetFrames = CountWavFrames(recSnap.LastWetPath);
                const int expectedFrames = 64 * frames;
                if (dryFrames != expectedFrames || wetFrames != expectedFrames)
                    throw new Exception($"Dual-Stream WAV frame count mismatch: dry={dryFrames}, wet={wetFrames}, expected={expectedFrames}");

                // 1-Click Live DI Re-amping: feed silence input, switch Twin Clean -> Recto+TS808 High-Gain
                var reampSnap = rec.SetReampState(enabled: true, playing: true, loop: true, seekSec: 0.0);
                if (!reampSnap.ReampAvailable || !reampSnap.ReampEnabled || !reampSnap.ReampPlaying)
                    throw new Exception("1-Click Re-amp did not activate cleanly after recording!");

                var cleanOut = new float[16 * frames];
                tone.Chain.UpdateChain(BuildAmpChain("amp_twin", "cab_v30", withOd: false));
                tone.ResetState();
                rec.SetReampState(seekSec: 0.0);
                long iAlloc0 = GC.GetAllocatedBytesForCurrentThread();
                for (int b = 0; b < 16; b++)
                {
                    Array.Clear(dl);
                    Array.Clear(dr);
                    rec.InjectReampDryRt(dl, dr, frames);
                    tone.Process(dl, dr, frames);
                    Array.Copy(dl, 0, cleanOut, b * frames, frames);
                }
                long injAlloc = GC.GetAllocatedBytesForCurrentThread() - iAlloc0;

                var rectoOut = new float[16 * frames];
                tone.Chain.UpdateChain(BuildAmpChain("amp_recto", "cab_mesa412", withOd: true));
                tone.ResetState();
                rec.SetReampState(seekSec: 0.0);
                for (int b = 0; b < 16; b++)
                {
                    Array.Clear(dl);
                    Array.Clear(dr);
                    rec.InjectReampDryRt(dl, dr, frames);
                    tone.Process(dl, dr, frames);
                    Array.Copy(dl, 0, rectoOut, b * frames, frames);
                }

                double cleanRms = Rms(cleanOut.AsSpan(4 * frames));
                double rectoRms = Rms(rectoOut.AsSpan(4 * frames));
                double diffSum = 0.0;
                for (int i = 4 * frames; i < cleanOut.Length; i++)
                    diffSum += Math.Abs(cleanOut[i] - rectoOut[i]);
                double meanDiff = diffSum / (12 * frames);

                if (injAlloc != 0 || cleanRms < 1e-3 || rectoRms < 1e-3 || meanDiff < 1e-3)
                    throw new Exception($"1-Click Live Re-amp verification failed: injAlloc={injAlloc}, cleanRms={cleanRms:F4}, rectoRms={rectoRms:F4}, diff={meanDiff:F4}");

                Console.WriteLine($"[PASS] Dual-Stream Recorder ({dryFrames}fr DryDI + WetStereo WAV) & 1-Click Live DI Re-amp (Twin rms={cleanRms:F3} vs Recto rms={rectoRms:F3}, diff={meanDiff:F3}, RT alloc: cap={capAlloc} B / inj={injAlloc} B)");
            }
            finally
            {
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true); } catch { }
            }
        }
    }

    static void VerifyMilestone5Bench()
    {
        const int sr = 48000;
        const int frames = 256;
        string root = Path.Combine(Path.GetTempPath(), $"nrs_m5_{Guid.NewGuid():N}");
        string modelsSrc = Path.Combine(root, "models_src");
        string modelsDst = Path.Combine(root, "models_dst");
        string modelsHub = Path.Combine(root, "models_hub");
        string irsDst = Path.Combine(root, "irs_dst");
        string irsHub = Path.Combine(root, "irs_hub");
        Directory.CreateDirectory(modelsSrc);
        Directory.CreateDirectory(modelsDst);
        Directory.CreateDirectory(modelsHub);
        Directory.CreateDirectory(irsDst);
        Directory.CreateDirectory(irsHub);

        try
        {
            // 1) .nrig Bundle Round-Trip + Multi-NAM Remapping + Custom Cab IR + SHA-256 Tamper Detection
            {
                var procA = new ToneProcessor { Mode = ToneMode.SignalChain };
                procA.Prepare(frames, sr);

                var hubRes1 = ToneHubService.InstallHubItem("hub_nam_dumble_ods", procA, targetSlotIndex: 2, modelsSrc, irsDst);
                var hubRes2 = ToneHubService.InstallHubItem("hub_nam_jcm800_jose", procA, targetSlotIndex: 10, modelsSrc, irsDst);
                var irRes = ToneHubService.InstallHubItem("hub_ir_greenback_8k", procA, -1, modelsSrc, irsDst);
                if (hubRes1.InstalledModel is null || hubRes2.InstalledModel is null || string.IsNullOrEmpty(irRes.CustomCabIrPath))
                    throw new Exception("Setup for .nrig export failed!");

                var chainA = AntigravityToneArchitect.EnsureScenes(new SignalChainConfigDto
                {
                    PresetId = "M5TEST",
                    PresetName = "Dual NAM Bundle Check",
                    ActiveScene = 0,
                    RoutingMode = (int)ChainRoutingMode.ParallelSplit,
                    SplitCol = 1,
                    MergeCol = 5,
                    ParallelMix = 0.5f,
                    TempoBpm = 136f,
                });
                chainA.Slots[2].ModelId = "nam_capture";
                chainA.Slots[2].NamModelId = hubRes1.InstalledModel.Id;
                chainA.Slots[2].Enabled = true;
                chainA.Slots[2].Params = new float[] { 0f, 0f, 100f, 0, 0, 0, 0, 0 };

                chainA.Slots[3].ModelId = "cab_custom";
                chainA.Slots[3].Enabled = true;
                chainA.Slots[3].Params = new float[] { 70f, 10500f, 0f, 0, 0, 0, 0, 0 };

                chainA.Slots[10].ModelId = "nam_capture";
                chainA.Slots[10].NamModelId = hubRes2.InstalledModel.Id;
                chainA.Slots[10].Enabled = true;
                chainA.Slots[10].Params = new float[] { 0f, 0f, 100f, 0, 0, 0, 0, 0 };

                chainA = AntigravityToneArchitect.CommitActiveScene(chainA);
                procA.Chain.UpdateChain(chainA);

                string nrigPath = Path.Combine(root, "DualNamRig.nrig");
                var manifest = NrigBundleService.ExportBundle(
                    nrigPath,
                    chainA,
                    procA,
                    procA.GetModelLibrarySnapshot(),
                    author: "RigBench",
                    description: "Verify WaveNet+LSTM+8K IR Bundle",
                    sampleRate: sr);

                if (manifest.NamModels.Count != 2 || manifest.CustomCabIr is null)
                    throw new Exception($".nrig export incomplete: namCount={manifest.NamModels.Count}, cab={(manifest.CustomCabIr != null)}");

                // SHA-256 tamper detection check
                string tamperedPath = Path.Combine(root, "TamperedRig.nrig");
                File.Copy(nrigPath, tamperedPath, overwrite: true);
                using (var z = new ZipArchive(File.Open(tamperedPath, FileMode.Open, FileAccess.ReadWrite), ZipArchiveMode.Update))
                {
                    var e = z.GetEntry(manifest.NamModels[0].EntryPath)!;
                    byte[] rawBytes;
                    using (var es = e.Open()) using (var ms = new MemoryStream()) { es.CopyTo(ms); rawBytes = ms.ToArray(); }
                    e.Delete();
                    var ne = z.CreateEntry(manifest.NamModels[0].EntryPath);
                    rawBytes[0] = (byte)(rawBytes[0] == '{' ? ' ' : '{'); // 1바이트 변조
                    using var ns = ne.Open();
                    ns.Write(rawBytes, 0, rawBytes.Length);
                }
                bool tamperCaught = false;
                try { NrigBundleService.InspectBundle(tamperedPath); }
                catch (InvalidDataException) { tamperCaught = true; }
                if (!tamperCaught)
                    throw new Exception(".nrig SHA-256 tamper detection failed to throw InvalidDataException!");

                // Clean-env Import and signal equality verification
                var procB = new ToneProcessor { Mode = ToneMode.SignalChain };
                procB.Prepare(frames, sr);
                var imp = NrigBundleService.ImportBundle(nrigPath, procB, modelsDst, irsDst);
                if (imp.ImportedModels.Count != 2 || !procB.HasCustomCabIr ||
                    string.IsNullOrEmpty(imp.SignalChain.Slots[2].NamModelId) ||
                    string.IsNullOrEmpty(imp.SignalChain.Slots[10].NamModelId))
                {
                    throw new Exception(".nrig clean import failed to remap Multi-NAM slots or Cab IR!");
                }

                procA.ResetState();
                procB.ResetState();
                float maxDiff = 0f;
                var lA = new float[frames];
                var rA = new float[frames];
                var lB = new float[frames];
                var rB = new float[frames];
                for (int b = 0; b < 8; b++)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        float s = 0.25f * MathF.Sin(2f * MathF.PI * 196f * (b * frames + i) / sr);
                        lA[i] = rA[i] = lB[i] = rB[i] = s;
                    }
                    procA.Process(lA, rA, frames);
                    procB.Process(lB, rB, frames);
                    for (int i = 0; i < frames; i++)
                    {
                        maxDiff = MathF.Max(maxDiff, MathF.Max(MathF.Abs(lA[i] - lB[i]), MathF.Abs(rA[i] - rB[i])));
                    }
                }
                if (maxDiff > 1e-4f)
                    throw new Exception($".nrig round-trip audio mismatch: maxDiff={maxDiff}");

                Console.WriteLine($"[PASS] .nrig Archive Round-Trip (WaveNet+LSTM+8K IR, SHA-256 tamper check + clean-env ID remap): maxDiff={maxDiff:E2}");
            }

            // 2) ToneHub Curated Vault (12 Items: WaveNet/LSTM/ConvNet + 8K IR + .nrig Artist Bundle)
            {
                var hubProc = new ToneProcessor { Mode = ToneMode.SignalChain };
                hubProc.Prepare(frames, sr);

                var catalog = ToneHubService.GetCatalog();
                if (catalog.Count < 12)
                    throw new Exception($"Expected >=12 curated Tone Hub items, got {catalog.Count}");

                // Test ConvNet NAM item
                var rConv = ToneHubService.InstallHubItem("hub_nam_mark2c_plus", hubProc, 2, modelsHub, irsHub);
                // Test WaveNet NAM item
                var rWn = ToneHubService.InstallHubItem("hub_nam_5150_block", hubProc, 2, modelsHub, irsHub);
                // Test 8K High-Res IR + .nrig Cathedral Worship Bundle (Dual-NAM + 8K IR + 126BPM)
                var rBundle = ToneHubService.InstallHubItem("hub_nrig_cathedral_worship", hubProc, -1, modelsHub, irsHub);

                var hl = new float[frames];
                var hr = new float[frames];
                for (int b = 0; b < 8; b++)
                {
                    for (int i = 0; i < frames; i++)
                        hl[i] = hr[i] = 0.22f * MathF.Sin(2f * MathF.PI * 220f * (b * frames + i) / sr);
                    hubProc.Process(hl, hr, frames);
                }
                double hubRms = Rms(hl);
                if (rConv.InstalledModel is null || rWn.InstalledModel is null || !hubProc.HasCustomCabIr ||
                    hubRms < 1e-3 || !double.IsFinite(hubRms))
                {
                    throw new Exception($"ToneHub install/audio check failed: rms={hubRms}");
                }

                Console.WriteLine($"[PASS] ToneHub Curated Vault ({catalog.Count} items: WaveNet/LSTM/ConvNet + 8K IR + Dual-NAM .nrig): Cathedral Worship rms={hubRms:F4}");
            }

            // 3) NativeAOT VST3 & CLAP Plugin Host Simulation + Sample-Accurate Automation + DAW State Save/Load + 0 B RT Alloc
            {
                if (ClapNativeEntry.EnsureInitialized() == 0 || Vst3NativeEntry.EnsureFactory() == 0)
                    throw new Exception("NativeAOT CLAP/VST3 C-ABI entry pointer was null!");

                // 3a) CLAP v1.2 Instance (Transport BPM sync + Sample-Accurate mid-block Scene/Gain automation + 0B alloc)
                using var clap = new ClapPluginInstance();
                clap.Activate(sr, 64, frames);
                clap.StartProcessing();

                var pluginChain = AntigravityToneArchitect.EnsureScenes(new SignalChainConfigDto
                {
                    PresetId = "DAW1",
                    PresetName = "DAW Automation Check",
                    ActiveScene = 0,
                    RoutingMode = (int)ChainRoutingMode.Serial,
                    TempoBpm = 120f,
                });
                // Scene A = Slot 1 Clean / Scene B = Slot 1 OD Active
                if (pluginChain.Scenes is { Count: >= 2 })
                {
                    pluginChain.Scenes[0].Slots[1].Enabled = false;
                    pluginChain.Scenes[1].Slots[1].Enabled = true;
                }
                pluginChain.Slots[1].Enabled = false;
                clap.Kernel.LoadChainConfig(pluginChain);

                var inL = new float[frames];
                var inR = new float[frames];
                var outL = new float[frames];
                var outR = new float[frames];

                Span<ClapEventParamValue> evs = stackalloc ClapEventParamValue[2];
                evs[0] = new ClapEventParamValue
                {
                    Header = new ClapEventHeader { Time = 64 },
                    ParamId = (uint)DawParamId.ActiveScene,
                    Value = 1.0, // Scene B mid-block
                };
                evs[1] = new ClapEventParamValue
                {
                    Header = new ClapEventHeader { Time = 192 },
                    ParamId = (uint)DawParamId.InputGainDb,
                    Value = 3.0,
                };

                // JIT Warmup (both normal Process & Event-split automation + Scene switch + Gains)
                for (int w = 0; w < 32; w++)
                {
                    for (int i = 0; i < frames; i++) inL[i] = inR[i] = 0.25f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                    evs[0].Value = (w & 1);
                    clap.Process(inL, inR, outL, outR, frames, hostTempoBpm: 138.0 + (w & 1) * 4.0, paramEvents: evs);
                }

                var sw = Stopwatch.StartNew();
                long cAlloc0 = GC.GetAllocatedBytesForCurrentThread();
                for (int k = 0; k < 200; k++)
                {
                    for (int i = 0; i < frames; i++) inL[i] = inR[i] = 0.25f * MathF.Sin(2f * MathF.PI * 196f * i / sr);
                    evs[0].Value = (k & 1);
                    evs[1].Value = (k % 5) - 2.0;
                    clap.Process(inL, inR, outL, outR, frames, hostTempoBpm: 140.0, paramEvents: evs);
                }
                long cAlloc = GC.GetAllocatedBytesForCurrentThread() - cAlloc0;
                sw.Stop();
                double clapMs = sw.Elapsed.TotalMilliseconds / 200.0;
                if (cAlloc != 0 || clapMs >= 1.2 || MathF.Abs(clap.Kernel.Processor.Chain.TempoBpm - 140f) > 0.1f)
                    throw new Exception($"CLAP RT/Automation check failed: alloc={cAlloc}, ms={clapMs:F4}, bpm={clap.Kernel.Processor.Chain.TempoBpm}");

                // 3b) VST3 v3.7 Instance + DAW State Save/Load Exact Equivalence + Package Exporter
                using var vstA = new Vst3PluginInstance();
                vstA.Initialize();
                vstA.SetupProcessing(sr, frames);
                vstA.SetActive(true);
                vstA.Kernel.LoadChainConfig(pluginChain);
                vstA.SetParamNormalized((uint)DawParamId.InputGainDb, vstA.PlainParamToNormalized((uint)DawParamId.InputGainDb, 3.5), 0);
                vstA.SetParamNormalized((uint)DawParamId.ActiveScene, vstA.PlainParamToNormalized((uint)DawParamId.ActiveScene, 1.0), 32);

                var vstCtx = new Vst3ProcessContext { StateFlags = 0x0400u, Tempo = 144.0 };
                for (int w = 0; w < 32; w++)
                {
                    for (int i = 0; i < frames; i++) inL[i] = inR[i] = 0.22f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
                    vstA.SetParamNormalized((uint)DawParamId.AmpGain, 0.55 + 0.05 * (w & 1), 64);
                    vstA.Process(inL, inR, outL, outR, frames, vstCtx);
                }
                long vAlloc0 = GC.GetAllocatedBytesForCurrentThread();
                for (int k = 0; k < 200; k++)
                {
                    for (int i = 0; i < frames; i++) inL[i] = inR[i] = 0.22f * MathF.Sin(2f * MathF.PI * 220f * i / sr);
                    vstA.SetParamNormalized((uint)DawParamId.Slot02Bypass, k % 2 == 0 ? 1.0 : 0.0, 128);
                    vstA.Process(inL, inR, outL, outR, frames, vstCtx);
                }
                long vAlloc = GC.GetAllocatedBytesForCurrentThread() - vAlloc0;
                if (vAlloc != 0)
                    throw new Exception($"VST3 RT automation allocated {vAlloc} B!");

                // State Save -> Load on fresh VST3 instance B
                byte[] dawState = vstA.GetState();
                using var vstB = new Vst3PluginInstance();
                vstB.Initialize();
                vstB.SetupProcessing(sr, frames);
                vstB.SetActive(true);
                if (!vstB.SetState(dawState))
                    throw new Exception("VST3 SetState failed!");

                vstA.Kernel.Processor.ResetState();
                vstB.Kernel.Processor.ResetState();
                var outL2 = new float[frames];
                var outR2 = new float[frames];
                float stateDiff = 0f;
                for (int b = 0; b < 6; b++)
                {
                    for (int i = 0; i < frames; i++) inL[i] = inR[i] = 0.2f * MathF.Sin(2f * MathF.PI * 261.6f * (b * frames + i) / sr);
                    vstA.Process(inL, inR, outL, outR, frames, vstCtx);
                    vstB.Process(inL, inR, outL2, outR2, frames, vstCtx);
                    for (int i = 0; i < frames; i++)
                        stateDiff = MathF.Max(stateDiff, MathF.Max(MathF.Abs(outL[i] - outL2[i]), MathF.Abs(outR[i] - outR2[i])));
                }
                if (stateDiff > 1e-5f)
                    throw new Exception($"VST3 State Save/Load mismatch: maxDiff={stateDiff}");

                string pkgDir = Path.Combine(root, "DawPkg");
                var pkg = DawPluginExporter.ExportPluginPackage(pkgDir, pluginChain, vstA.Kernel.Processor, vstA.Kernel.Processor.GetModelLibrarySnapshot());
                if (!File.Exists(pkg.Vst3ModuleInfoPath) || !File.Exists(pkg.ClapPluginPath) || !File.Exists(pkg.DefaultRigPath))
                    throw new Exception("DawPluginExporter output missing expected .vst3 / .clap artifacts!");

                Console.WriteLine($"[PASS] VST3/CLAP NativeAOT-Ready Plugins (32 Params, Host BPM=140/144, Sample-Accurate Automation, State Diff={stateDiff:E1}): {clapMs:F4} ms/block, CLAP={cAlloc} B / VST3={vAlloc} B");
            }
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    static void VerifyMcpSchematicAdditionBench()
    {
        const int sr = 48000;
        const int frames = 256;
        var preExistingDevices = CustomDeviceRegistry.GetAll().Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var createdDeviceIds = new List<string>();
        bool addedMa856 = false;
        try
        {
            // Check 1: Anti-Hallucination Missing Part Guard
            var missingCircuit = new SpiceCircuitDto
            {
                Stages = new List<SpiceStageDto>
                {
                    new()
                    {
                        Name = "missing_test",
                        Elements = new List<SpiceElementDto>
                        {
                            new() { Name = "D1", Type = "D", Nodes = new[] { "in", "out" }, Model = "UNKNOWN_PART_XYZ" },
                            new() { Name = "R1", Type = "R", Nodes = new[] { "out", "gnd" }, ValueText = "10k" },
                        },
                    },
                },
            };
            var missingRep = SpiceAnalyzer.AnalyzeCircuit(missingCircuit, sampleRate: sr);
            if (missingRep.Ok || !missingRep.MissingModels.Contains("UNKNOWN_PART_XYZ", StringComparer.OrdinalIgnoreCase))
                throw new Exception($"Missing-part guard failed: Ok={missingRep.Ok}, Missing={string.Join(",", missingRep.MissingModels)}");

            // Check 2: Datasheet .MODEL Validation & Registration ("MA856" diode)
            bool rejectedNoUrl = false;
            try
            {
                SpiceModelLibrary.RegisterOrUpdate(new SpiceComponentModelDto
                {
                    Name = "BAD_NO_URL",
                    Kind = "diode",
                    DatasheetUrl = "",
                    Params = new Dictionary<string, double> { ["Is"] = 2e-9, ["N"] = 1.75 },
                });
            }
            catch (InvalidOperationException) { rejectedNoUrl = true; }
            if (!rejectedNoUrl)
                throw new Exception("SpiceModelLibrary should reject models without datasheetUrl!");

            bool existedMa856 = SpiceModelLibrary.TryGet("MA856", out _);
            SpiceModelLibrary.RegisterOrUpdate(new SpiceComponentModelDto
            {
                Name = "MA856",
                Kind = "diode",
                DatasheetUrl = "https://datasheetspdf.com/pdf-file/1097600/Panasonic/MA856/1",
                Notes = "Panasonic silicon switching diode used in King of Tone / SD-1",
                Params = new Dictionary<string, double> { ["Is"] = 3.1e-9, ["N"] = 1.82, ["Rs"] = 0.65 },
            });
            addedMa856 = !existedMa856;

            var (foundParts, missingParts) = SpiceModelLibrary.CheckParts(new[] { "JRC4558", "1N914", "MA856" });
            if (foundParts.Length != 3 || missingParts.Length != 0)
                throw new Exception($"CheckParts failed after MA856 registration: found={foundParts.Length}, missing={missingParts.Length}");

            // Check 3: Real Tube Screamer (TS808) MNA SPICE Netlist (.OP + .AC Mid Hump + .TRAN THD + 0 B RT)
            var ts808Circuit = new SpiceCircuitDto
            {
                InputVoltageScale = 0.45f,
                OutputDcBlock = true,
                Knobs = new List<SpiceKnobDefDto>
                {
                    new() { Index = 0, Label = "DRIVE", Min = 0f, Max = 10f, Default = 6.5f },
                    new() { Index = 1, Label = "TONE", Min = 0f, Max = 10f, Default = 6.0f },
                    new() { Index = 2, Label = "LEVEL", Min = 0f, Max = 10f, Default = 7.0f },
                },
                Stages = new List<SpiceStageDto>
                {
                    new()
                    {
                        Name = "ts808_clipper",
                        InputNode = "in",
                        OutputNode = "out",
                        InputSeriesResistance = 1000f,
                        Supplies = new List<SpiceFixedVoltageDto> { new() { Node = "vref", Voltage = 4.5 } },
                        Elements = new List<SpiceElementDto>
                        {
                            new() { Name = "C2", Type = "C", Nodes = new[] { "in", "base" }, ValueText = "1u" },
                            new() { Name = "R3", Type = "R", Nodes = new[] { "base", "vref" }, ValueText = "510k" },
                            new() { Name = "R4", Type = "R", Nodes = new[] { "neg", "n47" }, ValueText = "4.7k" },
                            new() { Name = "C3", Type = "C", Nodes = new[] { "n47", "vref" }, ValueText = "47n" },
                            new() { Name = "R6", Type = "R", Nodes = new[] { "neg", "fb_mid" }, ValueText = "51k" },
                            new() { Name = "P_DRV", Type = "POT", Nodes = new[] { "nc", "fb_mid", "out" }, ValueText = "500k", KnobIndex = 0, Taper = "LOG" },
                            new() { Name = "C4", Type = "C", Nodes = new[] { "neg", "out" }, ValueText = "51p" },
                            new() { Name = "D1", Type = "D", Nodes = new[] { "neg", "out" }, Model = "1N914" },
                            new() { Name = "D2", Type = "D", Nodes = new[] { "out", "neg" }, Model = "1N914" },
                            new() { Name = "U1", Type = "U", Nodes = new[] { "base", "neg", "out" }, Model = "JRC4558" },
                        },
                    },
                    new()
                    {
                        Name = "ts808_tone_level",
                        InputNode = "in",
                        OutputNode = "out",
                        InputSeriesResistance = 1000f,
                        Elements = new List<SpiceElementDto>
                        {
                            new() { Name = "R_T", Type = "R", Nodes = new[] { "in", "lp" }, ValueText = "1k" },
                            new() { Name = "C_T", Type = "C", Nodes = new[] { "lp", "gnd" }, ValueText = "220n" },
                            new() { Name = "P_TONE", Type = "POT", Nodes = new[] { "in", "tone_out", "lp" }, ValueText = "20k", KnobIndex = 1, Taper = "LIN" },
                            new() { Name = "P_LVL", Type = "POT", Nodes = new[] { "tone_out", "out", "gnd" }, ValueText = "100k", KnobIndex = 2, Taper = "LOG" },
                            new() { Name = "R_LOAD", Type = "R", Nodes = new[] { "out", "gnd" }, ValueText = "100k" },
                        },
                    },
                },
            };
            var tsRep = SpiceAnalyzer.AnalyzeCircuit(ts808Circuit, sampleRate: sr, testFreqHz: 440f, testAmp: 0.25f);
            if (!tsRep.Ok || tsRep.Transient == null || !tsRep.Transient.IsStable || tsRep.RtAllocBytes != 0)
                throw new Exception($"TS808 SPICE sim failed: {tsRep.Summary}");
            double opOut = tsRep.OperatingPoints[0].OutputNodeDcV;
            if (Math.Abs(opOut - 4.5) > 0.15)
                throw new Exception($"TS808 .OP Vbias out={opOut:F3}V expected ~4.50V!");
            double g100 = tsRep.AcSweep.OrderBy(p => Math.Abs(p.FreqHz - 100)).First().GainDb;
            double g720 = tsRep.AcSweep.OrderBy(p => Math.Abs(p.FreqHz - 720)).First().GainDb;
            if ((g720 - g100) < 4.0 || tsRep.Transient.ThdPct < 5.0)
                throw new Exception($"TS808 physics check failed: 720Hz-100Hz={g720 - g100:F2}dB, THD={tsRep.Transient.ThdPct:F2}%");

            // Check 4: Real Dallas Fuzz Face (2x AC128 PNP Germanium BJT Ebers-Moll)
            var fuzzFaceCircuit = new SpiceCircuitDto
            {
                InputVoltageScale = 0.25f,
                OutputDcBlock = true,
                Knobs = new List<SpiceKnobDefDto>
                {
                    new() { Index = 0, Label = "FUZZ", Min = 0f, Max = 10f, Default = 8.5f },
                    new() { Index = 1, Label = "VOLUME", Min = 0f, Max = 10f, Default = 6.0f },
                },
                Stages = new List<SpiceStageDto>
                {
                    new()
                    {
                        Name = "fuzz_face_ac128",
                        InputNode = "in",
                        OutputNode = "out",
                        InputSeriesResistance = 1000f,
                        Supplies = new List<SpiceFixedVoltageDto> { new() { Node = "vneg", Voltage = -9.0 } },
                        Elements = new List<SpiceElementDto>
                        {
                            new() { Name = "C1", Type = "C", Nodes = new[] { "in", "b1" }, ValueText = "2.2u" },
                            new() { Name = "R_PD", Type = "R", Nodes = new[] { "in", "gnd" }, ValueText = "1M" },
                            new() { Name = "Q1", Type = "Q", Nodes = new[] { "c1", "b1", "gnd" }, Model = "AC128" },
                            new() { Name = "R_C1", Type = "R", Nodes = new[] { "c1", "vneg" }, ValueText = "33k" },
                            new() { Name = "R_FB", Type = "R", Nodes = new[] { "e2", "b1" }, ValueText = "100k" },
                            new() { Name = "Q2", Type = "Q", Nodes = new[] { "c2", "c1", "e2" }, Model = "AC128" },
                            new() { Name = "P_FUZZ", Type = "POT", Nodes = new[] { "e2", "f_w", "gnd" }, ValueText = "1k", KnobIndex = 0, Taper = "LIN" },
                            new() { Name = "C_E2", Type = "C", Nodes = new[] { "f_w", "gnd" }, ValueText = "20u" },
                            new() { Name = "R_C2A", Type = "R", Nodes = new[] { "c2", "col_tap" }, ValueText = "470" },
                            new() { Name = "R_C2B", Type = "R", Nodes = new[] { "col_tap", "vneg" }, ValueText = "8.2k" },
                            new() { Name = "C_OUT", Type = "C", Nodes = new[] { "col_tap", "out_top" }, ValueText = "10n" },
                            new() { Name = "P_VOL", Type = "POT", Nodes = new[] { "out_top", "out", "gnd" }, ValueText = "500k", KnobIndex = 1, Taper = "LOG" },
                            new() { Name = "R_LOAD", Type = "R", Nodes = new[] { "out", "gnd" }, ValueText = "500k" },
                        },
                    },
                },
            };
            var ffRep = SpiceAnalyzer.AnalyzeCircuit(fuzzFaceCircuit, sampleRate: sr, testFreqHz: 220f, testAmp: 0.25f);
            if (!ffRep.Ok || ffRep.Transient == null || !ffRep.Transient.IsStable || ffRep.RtAllocBytes != 0 || ffRep.Transient.ThdPct < 10.0)
                throw new Exception($"Fuzz Face AC128 SPICE sim failed: {ffRep.Summary}");

            // Check 5: Real 12AX7 Koren Triode Stage (250V B+) + Measured-Curve Greenback Cab + Simultaneous 0 B RT Chain
            var triodeAmpCircuit = new SpiceCircuitDto
            {
                InputVoltageScale = 1.2f,
                OutputVoltageScale = 0.08f,
                OutputDcBlock = true,
                Knobs = new List<SpiceKnobDefDto>
                {
                    new() { Index = 0, Label = "GAIN", Min = 0f, Max = 10f, Default = 7.5f },
                    new() { Index = 1, Label = "MASTER", Min = 0f, Max = 10f, Default = 6.0f },
                },
                Stages = new List<SpiceStageDto>
                {
                    new()
                    {
                        Name = "12ax7_stage1",
                        InputNode = "in",
                        OutputNode = "out",
                        InputSeriesResistance = 1000f,
                        Supplies = new List<SpiceFixedVoltageDto> { new() { Node = "bplus", Voltage = 250.0 } },
                        Elements = new List<SpiceElementDto>
                        {
                            new() { Name = "C_IN", Type = "C", Nodes = new[] { "in", "g_in" }, ValueText = "22n" },
                            new() { Name = "P_GAIN", Type = "POT", Nodes = new[] { "g_in", "g1", "gnd" }, ValueText = "1M", KnobIndex = 0, Taper = "LOG" },
                            new() { Name = "R_STOP", Type = "R", Nodes = new[] { "g1", "grid" }, ValueText = "68k" },
                            new() { Name = "T1", Type = "T", Nodes = new[] { "plate", "grid", "cath" }, Model = "12AX7" },
                            new() { Name = "R_P", Type = "R", Nodes = new[] { "bplus", "plate" }, ValueText = "100k" },
                            new() { Name = "R_K", Type = "R", Nodes = new[] { "cath", "gnd" }, ValueText = "1.5k" },
                            new() { Name = "C_K", Type = "C", Nodes = new[] { "cath", "gnd" }, ValueText = "22u" },
                            new() { Name = "C_OUT", Type = "C", Nodes = new[] { "plate", "out_top" }, ValueText = "22n" },
                            new() { Name = "P_MV", Type = "POT", Nodes = new[] { "out_top", "out", "gnd" }, ValueText = "1M", KnobIndex = 1, Taper = "LOG" },
                            new() { Name = "R_LD", Type = "R", Nodes = new[] { "out", "gnd" }, ValueText = "1M" },
                        },
                    },
                },
            };
            var triodeRep = SpiceAnalyzer.AnalyzeCircuit(triodeAmpCircuit, sampleRate: sr, testFreqHz: 220f, testAmp: 0.35f);
            if (!triodeRep.Ok || triodeRep.Transient == null || !triodeRep.Transient.IsStable || triodeRep.RtAllocBytes != 0)
                throw new Exception($"12AX7 Triode SPICE sim failed: {triodeRep.Summary}");
            if (!triodeRep.OperatingPoints[0].NodeVoltagesDc.TryGetValue("plate", out double vPlate) || vPlate < 90.0 || vPlate > 235.0)
                throw new Exception($"12AX7 .OP plate voltage={vPlate:F1}V outside physical range [90V..235V]!");

            var cabSpecDto = new CustomCabAcousticDto
            {
                EnclosureType = "4x12 Closed Back",
                ResonanceHz = 75f,
                HighRollOffHz = 4750f,
                ErDelayMs = 1.45f,
                ErGain = -0.10f,
                MeasuredCurve = new List<FreqResponsePointDto>
                {
                    new() { FreqHz = 50f, Db = -9.5f },
                    new() { FreqHz = 75f, Db = -1.5f },
                    new() { FreqHz = 115f, Db = 2.0f },
                    new() { FreqHz = 450f, Db = -0.5f },
                    new() { FreqHz = 1000f, Db = 0.0f },
                    new() { FreqHz = 2400f, Db = 4.2f },
                    new() { FreqHz = 4200f, Db = -2.0f },
                    new() { FreqHz = 6500f, Db = -16.0f },
                },
            };
            var cabRep = SpiceAnalyzer.AnalyzeCabinet(cabSpecDto, sr);
            if (!cabRep.Ok || cabRep.RtAllocBytes != 0)
                throw new Exception($"Cab curve sim failed: {cabRep.Summary}");

            var devPedal = CustomDeviceRegistry.RegisterOrUpdate(new CustomDeviceSpecDto
            {
                Id = "custom_drive_bench_ts808_spice",
                Name = "Bench TS808 [SPICE]",
                ShortName = "BenchTS808",
                Category = "drive",
                SchematicSourceUrl = "https://www.electrosmash.com/tube-screamer-analysis",
                SchematicSummary = "JRC4558 + 1N914 + 4.7k/47nF MNA SPICE",
                SpiceCircuit = ts808Circuit,
            });
            var devAmp = CustomDeviceRegistry.RegisterOrUpdate(new CustomDeviceSpecDto
            {
                Id = "custom_amp_bench_12ax7_spice",
                Name = "Bench 12AX7 Triode [SPICE]",
                ShortName = "Bench12AX7",
                Category = "amp",
                SchematicSourceUrl = "https://robrobinette.com/",
                SchematicSummary = "12AX7 Koren Triode + 250V B+ + 100k/1.5k",
                SpiceCircuit = triodeAmpCircuit,
            });
            var devCab = CustomDeviceRegistry.RegisterOrUpdate(new CustomDeviceSpecDto
            {
                Id = "custom_cab_bench_greenback_curve",
                Name = "Bench Greenback Curve [IR]",
                ShortName = "BenchGBK",
                Category = "cab",
                SchematicSourceUrl = "https://celestion.com/product/g12m-greenback/",
                SchematicSummary = "Celestion G12M-25 measured SPL curve minimum-phase IR",
                CabAcoustic = cabSpecDto,
            });
            foreach (var id in new[] { devPedal.Id, devAmp.Id, devCab.Id })
            {
                if (!preExistingDevices.Contains(id)) createdDeviceIds.Add(id);
                if (!AntigravityToneArchitect.TryFindDevice(id, out _))
                    throw new Exception($"Registered SPICE device '{id}' missing from AntigravityToneArchitect.Catalog!");
            }

            // Full SignalChain RT Execution Test (TS808 SPICE -> 12AX7 SPICE -> Measured-Curve Cab IR)
            var proc = new ToneProcessor { Mode = ToneMode.SignalChain, GateThresholdDb = -80f };
            proc.Prepare(frames, sr);
            var chain = new SignalChainConfigDto { RoutingMode = (int)ChainRoutingMode.Serial };
            for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
                chain.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = false, Params = new float[8] };
            chain.Slots[1] = new ChainSlotDto { Index = 1, ModelId = devPedal.Id, Enabled = true, Params = AntigravityToneArchitect.CreateDefaultParams(devPedal.Id) };
            chain.Slots[2] = new ChainSlotDto { Index = 2, ModelId = devAmp.Id, Enabled = true, Params = AntigravityToneArchitect.CreateDefaultParams(devAmp.Id) };
            chain.Slots[3] = new ChainSlotDto { Index = 3, ModelId = devCab.Id, Enabled = true, Params = AntigravityToneArchitect.CreateDefaultParams(devCab.Id) };
            chain = AntigravityToneArchitect.CommitActiveScene(chain);
            proc.Chain.UpdateChain(chain);

            var bufL = new float[frames];
            var bufR = new float[frames];
            for (int w = 0; w < 32; w++)
            {
                for (int i = 0; i < frames; i++)
                    bufL[i] = bufR[i] = 0.22f * MathF.Sin(2f * MathF.PI * 196f * (w * frames + i) / sr);
                proc.Process(bufL, bufR, frames);
            }

            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 200; k++)
            {
                if ((k & 31) == 31)
                    proc.Chain.SetPrimaryAmpParamRt(0, 6.0f + (k % 3)); // ensure live RT knob handling compiles cleanly too
                for (int i = 0; i < frames; i++)
                    bufL[i] = bufR[i] = 0.22f * MathF.Sin(2f * MathF.PI * 196f * ((k + 32) * frames + i) / sr);
                proc.Process(bufL, bufR, frames);
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 200.0;
            double outRms = Rms(bufL);

            if (alloc != 0 || ms >= 1.2 || !double.IsFinite(outRms) || outRms < 1e-4)
                throw new Exception($"SPICE chain RT execution failed: alloc={alloc}, ms={ms:F4}, rms={outRms}");

            Console.WriteLine($"[PASS] AI-Led MNA SPICE Engine (TS808 OP={opOut:F2}V/+4dB@720Hz + AC128 Fuzz THD={ffRep.Transient.ThdPct:F1}% + 12AX7 Vp={vPlate:F1}V + Cab Curve + Guard): {ms:F4} ms/block, rms={outRms:F4}, RT alloc: {alloc} B");
        }
        finally
        {
            foreach (var id in createdDeviceIds)
                CustomDeviceRegistry.Remove(id);
            if (addedMa856)
                SpiceModelLibrary.RemoveCustom("MA856");
        }
    }
}
