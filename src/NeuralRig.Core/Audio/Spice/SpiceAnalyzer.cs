using System.Diagnostics;

namespace NeuralRigStudio.Audio.Spice;

public sealed class SpiceAcPoint
{
    public double FreqHz { get; init; }
    public double GainDb { get; init; }
    public double PhaseDeg { get; init; }
}

public sealed class SpiceStageOpReport
{
    public string StageName { get; init; } = "";
    public Dictionary<string, double> NodeVoltagesDc { get; init; } = new();
    public double OutputNodeDcV { get; init; }
}

public sealed class SpiceTransientReport
{
    public float TestFreqHz { get; init; }
    public float TestAmplitude { get; init; }
    public double OutputRms { get; init; }
    public double PeakPositive { get; init; }
    public double PeakNegative { get; init; }
    public double AsymmetryPct { get; init; }
    public double ThdPct { get; init; }
    public double H2DbRelativeToFundamental { get; init; }
    public double H3DbRelativeToFundamental { get; init; }
    public double EvenToOddDb { get; init; }
    public double DcOffset { get; init; }
    public bool IsStable { get; init; }
}

public sealed class SpiceSimulationReport
{
    public bool Ok { get; init; }
    public int SampleRate { get; init; }
    public int OversampledRate => SampleRate * 4;
    public string[] Errors { get; init; } = Array.Empty<string>();
    public string[] MissingModels { get; init; } = Array.Empty<string>();
    public SpiceStageOpReport[] OperatingPoints { get; init; } = Array.Empty<SpiceStageOpReport>();
    public SpiceAcPoint[] AcSweep { get; init; } = Array.Empty<SpiceAcPoint>();
    public double AcPeakFreqHz { get; init; }
    public double AcPeakGainDb { get; init; }
    public double AcGainAt1kHzDb { get; init; }
    public SpiceTransientReport? Transient { get; init; }
    public double RtBlockMs { get; init; }
    public long RtAllocBytes { get; init; }
    public string Summary { get; init; } = "";
}

public sealed class CabSimulationReport
{
    public bool Ok { get; init; }
    public string[] Errors { get; init; } = Array.Empty<string>();
    public int TapCount { get; init; }
    public double PeakFreqHz { get; init; }
    public double LowRolloffMinus3DbHz { get; init; }
    public double HighRolloffMinus3DbHz { get; init; }
    public long RtAllocBytes { get; init; }
    public double RtBlockMs { get; init; }
    public string Summary { get; init; } = "";
}

/// <summary>
/// AI 에이전트가 작성한 SPICE 넷리스트 및 캐비넷 곡선을 커밋 전에 검증하는
/// .OP(직류 바이어스), .AC(복소 보드 플롯), .TRAN(4x 비선형 고조파/안정성), RT 무할당 벤치마크 엔진.
/// </summary>
public static class SpiceAnalyzer
{
    private static readonly double[] AcFreqs =
    {
        30, 50, 80, 120, 200, 350, 500, 720, 1000, 1500, 2200, 3500, 5000, 8000, 12000, 16000
    };

    public static SpiceSimulationReport AnalyzeCircuit(
        SpiceCircuitDto dto,
        float[]? knobs = null,
        int sampleRate = 48000,
        float testFreqHz = 440f,
        float testAmp = 0.25f)
    {
        sampleRate = Math.Clamp(sampleRate, 44100, 96000);
        int fs4 = sampleRate * 4;
        var compiled = RealtimeSpiceCircuit.Compile(dto, fs4);
        if (!compiled.Ok || compiled.Circuit == null)
        {
            return new SpiceSimulationReport
            {
                Ok = false,
                SampleRate = sampleRate,
                Errors = compiled.Errors,
                MissingModels = compiled.MissingModels,
                Summary = compiled.MissingModels.Length > 0
                    ? $"미등록 부품 .MODEL 감지: {string.Join(", ", compiled.MissingModels)}"
                    : string.Join("; ", compiled.Errors),
            };
        }

        var rt = compiled.Circuit;
        var k = new float[8];
        for (int i = 0; i < 8; i++) k[i] = 5f;
        if (dto.Knobs != null)
        {
            foreach (var kd in dto.Knobs)
                if (kd.Index is >= 0 and < 8) k[kd.Index] = kd.Default;
        }
        if (knobs != null)
        {
            for (int i = 0; i < Math.Min(8, knobs.Length); i++)
                if (float.IsFinite(knobs[i])) k[i] = knobs[i];
        }
        rt.PrepareForAnalysis(k);

        // 1) .OP (DC Operating Point)
        var ops = rt.Stages.Select(st => new SpiceStageOpReport
        {
            StageName = st.Name,
            NodeVoltagesDc = st.GetDcOperatingPointSnapshot(),
            OutputNodeDcV = Math.Round(st.DcOutputVoltage, 4),
        }).ToArray();

        // 2) .AC Complex Frequency Sweep
        var acPoints = new List<SpiceAcPoint>(AcFreqs.Length);
        double peakDb = -200.0, peakHz = 1000.0, gain1k = 0.0;
        foreach (double f in AcFreqs)
        {
            double dbSum = 0.0, phaseSum = 0.0;
            foreach (var st in rt.Stages)
            {
                var (gDb, ph) = st.ComputeAcTransfer(f);
                dbSum += gDb;
                phaseSum += ph;
            }
            while (phaseSum > 180.0) phaseSum -= 360.0;
            while (phaseSum < -180.0) phaseSum += 360.0;

            double rDb = Math.Round(dbSum, 2);
            double rPh = Math.Round(phaseSum, 1);
            acPoints.Add(new SpiceAcPoint { FreqHz = f, GainDb = rDb, PhaseDeg = rPh });
            if (dbSum > peakDb) { peakDb = dbSum; peakHz = f; }
            if (Math.Abs(f - 1000.0) < 1.0) gain1k = rDb;
        }

        // 3) .TRAN 4x HIIR Oversampled Harmonic / Asymmetry Analysis
        rt.Reset();
        float fTest = Math.Clamp(testFreqHz, 60f, 3000f);
        float ampTest = Math.Clamp(testAmp, 0.02f, 1.0f);
        const int frames = 256;
        var os = new SchematicPedalDsp.SlotOversampler4x();
        var scratch2x = new double[frames * 2];
        var scratch4x = new float[frames * 4];
        var block1x = new float[frames];

        // Capture >= 24 blocks (~6144 1x samples = ~128 ms), analyze trailing integer cycles at 1x sample rate
        var captured = new List<float>(24 * frames);
        bool stable = true;
        int sampleCursor = 0;
        for (int b = 0; b < 24; b++)
        {
            for (int i = 0; i < frames; i++)
            {
                block1x[i] = ampTest * MathF.Sin(2f * MathF.PI * fTest * sampleCursor / sampleRate);
                sampleCursor++;
            }
            var buf4 = os.Upsample(block1x, frames, scratch2x, scratch4x);
            rt.ProcessBlock4x(buf4, k[0], k[1], k[2], k[3], k[4], k[5], k[6], k[7]);
            os.Downsample(block1x, frames, scratch2x, scratch4x);
            for (int i = 0; i < frames; i++)
            {
                if (!float.IsFinite(block1x[i])) stable = false;
                captured.Add(block1x[i]);
            }
        }

        double period1x = (double)sampleRate / fTest;
        int usableStart = Math.Min(captured.Count / 2, 2048);
        int avail = captured.Count - usableStart;
        int cycles = Math.Max(4, (int)Math.Floor(avail / period1x));
        int winLen = Math.Min(avail, (int)Math.Round(cycles * period1x));
        double rms = 0.0, posMax = 0.0, negMax = 0.0, sumDc = 0.0;
        for (int i = 0; i < winLen; i++)
        {
            double v = captured[usableStart + i];
            sumDc += v;
            rms += v * v;
            if (v > posMax) posMax = v;
            if (-v > negMax) negMax = -v;
        }
        rms = Math.Sqrt(rms / Math.Max(1, winLen));
        double dcOff = sumDc / Math.Max(1, winLen);
        double asymPct = (posMax + negMax) > 1e-6 ? Math.Abs(posMax - negMax) / Math.Max(posMax, negMax) * 100.0 : 0.0;

        // Exact harmonic projection up to H7
        double HarmonicMag(int h)
        {
            double w = 2.0 * Math.PI * fTest * h / sampleRate;
            double cSum = 0.0, sSum = 0.0;
            for (int i = 0; i < winLen; i++)
            {
                double v = captured[usableStart + i] - dcOff;
                cSum += v * Math.Cos(w * i);
                sSum += v * Math.Sin(w * i);
            }
            return (2.0 / Math.Max(1, winLen)) * Math.Sqrt(cSum * cSum + sSum * sSum);
        }

        double h1 = Math.Max(1e-8, HarmonicMag(1));
        double h2 = HarmonicMag(2);
        double h3 = HarmonicMag(3);
        double harmEnergy = h2 * h2 + h3 * h3;
        double evenEnergy = h2 * h2, oddEnergy = h3 * h3;
        for (int h = 4; h <= 7; h++)
        {
            double hm = HarmonicMag(h);
            harmEnergy += hm * hm;
            if ((h & 1) == 0) evenEnergy += hm * hm;
            else oddEnergy += hm * hm;
        }
        double thdPct = (Math.Sqrt(harmEnergy) / h1) * 100.0;
        double h2Db = 20.0 * Math.Log10(Math.Max(1e-6, h2 / h1));
        double h3Db = 20.0 * Math.Log10(Math.Max(1e-6, h3 / h1));
        double evenOddDb = 10.0 * Math.Log10(Math.Max(1e-6, evenEnergy) / Math.Max(1e-6, oddEnergy));

        var tran = new SpiceTransientReport
        {
            TestFreqHz = fTest,
            TestAmplitude = ampTest,
            OutputRms = Math.Round(rms, 5),
            PeakPositive = Math.Round(posMax, 5),
            PeakNegative = Math.Round(negMax, 5),
            AsymmetryPct = Math.Round(asymPct, 2),
            ThdPct = Math.Round(thdPct, 2),
            H2DbRelativeToFundamental = Math.Round(h2Db, 2),
            H3DbRelativeToFundamental = Math.Round(h3Db, 2),
            EvenToOddDb = Math.Round(evenOddDb, 2),
            DcOffset = Math.Round(dcOff, 6),
            IsStable = stable && double.IsFinite(rms) && rms < 10.0,
        };

        // 4) RT Allocation & CPU Benchmark (256 frames x 64 blocks after 16 warmup blocks)
        rt.Reset();
        os.Reset();
        for (int i = 0; i < frames; i++)
            block1x[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sampleRate);

        for (int w = 0; w < 16; w++)
        {
            var b4 = os.Upsample(block1x, frames, scratch2x, scratch4x);
            rt.ProcessBlock4x(b4, k[0], k[1], k[2], k[3], k[4], k[5], k[6], k[7]);
            os.Downsample(block1x, frames, scratch2x, scratch4x);
            for (int i = 0; i < frames; i++) block1x[i] = 0.2f * MathF.Sin(2f * MathF.PI * 196f * i / sampleRate);
        }

        var sw = Stopwatch.StartNew();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (int b = 0; b < 64; b++)
        {
            var b4 = os.Upsample(block1x, frames, scratch2x, scratch4x);
            rt.ProcessBlock4x(b4, k[0], k[1], k[2], k[3], k[4], k[5], k[6], k[7]);
            os.Downsample(block1x, frames, scratch2x, scratch4x);
            // Vary knob every few blocks to confirm 0 B allocation even during Schur refactor!
            if ((b & 7) == 7) k[0] = Math.Clamp(k[0] + 0.01f, 0f, 10f);
        }
        long rtAlloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        sw.Stop();
        double msPerBlock = sw.Elapsed.TotalMilliseconds / 64.0;

        bool ok = tran.IsStable && rtAlloc == 0 && msPerBlock < 3.0;
        string summary =
            $".OP {ops.Length}stage · .AC Peak={peakHz:F0}Hz({peakDb:F1}dB), 1kHz={gain1k:F1}dB · .TRAN THD={thdPct:F1}%(H2={h2Db:F1}dB,H3={h3Db:F1}dB,Asym={asymPct:F1}%) · RT={msPerBlock:F3}ms/blk, Alloc={rtAlloc}B";

        return new SpiceSimulationReport
        {
            Ok = ok,
            SampleRate = sampleRate,
            OperatingPoints = ops,
            AcSweep = acPoints.ToArray(),
            AcPeakFreqHz = peakHz,
            AcPeakGainDb = Math.Round(peakDb, 2),
            AcGainAt1kHzDb = gain1k,
            Transient = tran,
            RtBlockMs = Math.Round(msPerBlock, 4),
            RtAllocBytes = rtAlloc,
            Summary = summary,
        };
    }

    public static CabSimulationReport AnalyzeCabinet(CustomCabAcousticDto? cab, int sampleRate = 48000)
    {
        if (cab == null)
        {
            return new CabSimulationReport
            {
                Ok = false,
                Errors = new[] { "cabAcoustic 객체가 필요합니다." },
            };
        }

        PartitionedIr? partIr = null;
        if (!string.IsNullOrWhiteSpace(cab.WavIrPath))
        {
            if (!CabinetIrBank.TryLoadWavIrFromFile(cab.WavIrPath, sampleRate, out partIr) || partIr == null)
            {
                return new CabSimulationReport
                {
                    Ok = false,
                    Errors = new[] { $"WAV IR 파일을 로드할 수 없습니다: {cab.WavIrPath}" },
                };
            }
        }
        else if (cab.MeasuredCurve != null && cab.MeasuredCurve.Count >= 3)
        {
            var pts = new (double f, double db)[cab.MeasuredCurve.Count];
            for (int i = 0; i < cab.MeasuredCurve.Count; i++)
                pts[i] = (Math.Max(15.0, cab.MeasuredCurve[i].FreqHz), cab.MeasuredCurve[i].Db);
            partIr = CabinetIrBank.CompileCustomCabFromMeasuredCurve(
                sampleRate, pts, Math.Clamp(cab.ErDelayMs, 0.8f, 4.0f), Math.Clamp(cab.ErGain, -0.35f, 0.35f));
        }
        else
        {
            partIr = CabinetIrBank.CompileCustomCabIr(
                sampleRate,
                Math.Clamp(cab.ResonanceHz, 50f, 130f),
                cab.EnclosureType.Contains("Open", StringComparison.OrdinalIgnoreCase) ? 1.65 : 1.35,
                Math.Clamp(cab.ResonanceHz * 1.45, 85f, 175f),
                0.55,
                2200.0,
                0.60,
                Math.Clamp(cab.HighRollOffHz, 3200f, 8200f),
                Math.Clamp(cab.ErDelayMs, 0.8f, 3.5f));
        }

        // RT benchmark on StereoPartitionedConvolver
        const int frames = 256;
        var conv = new StereoPartitionedConvolver();
        conv.Prepare(frames);
        var bufL = new float[frames];
        var bufR = new float[frames];
        for (int w = 0; w < 8; w++)
        {
            for (int i = 0; i < frames; i++) bufL[i] = bufR[i] = i == 0 && w == 0 ? 1f : 0.1f * MathF.Sin(0.05f * i);
            conv.ConvolveStereo(bufL, bufR, frames, partIr);
        }

        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int b = 0; b < 64; b++)
            conv.ConvolveStereo(bufL, bufR, frames, partIr);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds / 64.0;

        // Compute frequency curve stats from MeasuredCurve or nominal parameters
        double peakHz = cab.MeasuredCurve is { Count: > 0 }
            ? cab.MeasuredCurve.OrderByDescending(x => x.Db).First().FreqHz
            : cab.ResonanceHz * 1.45;
        double lowHz = Math.Clamp(cab.ResonanceHz, 50f, 140f);
        double hiHz = Math.Clamp(cab.HighRollOffHz, 3000f, 8500f);

        int tapCount = partIr.RawIr.Length;
        return new CabSimulationReport
        {
            Ok = alloc == 0,
            TapCount = tapCount,
            PeakFreqHz = Math.Round(peakHz, 1),
            LowRolloffMinus3DbHz = Math.Round(lowHz, 1),
            HighRolloffMinus3DbHz = Math.Round(hiHz, 1),
            RtAllocBytes = alloc,
            RtBlockMs = Math.Round(ms, 4),
            Summary = $"Cab IR {tapCount} taps (Peak={peakHz:F0}Hz, Roll-off={lowHz:F0}Hz..{hiHz:F0}Hz), RT={ms:F3}ms/blk, Alloc={alloc}B",
        };
    }
}
