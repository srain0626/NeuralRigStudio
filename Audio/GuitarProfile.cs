namespace NeuralRigStudio.Audio;

public sealed class GuitarNoteMeasurementDto
{
    public string StepId { get; set; } = "";
    public string TargetLabel { get; set; } = "";
    public string TargetNote { get; set; } = "";
    public float TargetFreqHz { get; set; }
    public float DetectedFreqHz { get; set; }
    public float CentsOffset { get; set; }
    public float PeakDb { get; set; }
    public float RmsDb { get; set; }
    public float SustainSec { get; set; }
    public float SpectralCentroidHz { get; set; }
    public float H2RatioDb { get; set; }
    public float H3RatioDb { get; set; }
    public bool Skipped { get; set; }

    public GuitarNoteMeasurementDto Clone() => new()
    {
        StepId = StepId,
        TargetLabel = TargetLabel,
        TargetNote = TargetNote,
        TargetFreqHz = TargetFreqHz,
        DetectedFreqHz = DetectedFreqHz,
        CentsOffset = CentsOffset,
        PeakDb = PeakDb,
        RmsDb = RmsDb,
        SustainSec = SustainSec,
        SpectralCentroidHz = SpectralCentroidHz,
        H2RatioDb = H2RatioDb,
        H3RatioDb = H3RatioDb,
        Skipped = Skipped,
    };
}

public sealed class GuitarProfileDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "내 기타";
    public string CreatedAt { get; set; } = "";
    public string EstimatedPickupType { get; set; } = "";
    public float NoiseFloorDb { get; set; } = -75f;
    public float Hum60HzDb { get; set; } = -80f;
    public float SuggestedGateDb { get; set; } = -65f;
    public float PeakDb { get; set; } = -12f;
    public float RmsDb { get; set; } = -22f;
    public float CrestFactorDb { get; set; } = 10f;
    public float SuggestedInputTrimDb { get; set; } = 0f;
    public int BrightnessScore { get; set; } = 50;
    public int WarmthScore { get; set; } = 50;
    public int OutputPowerScore { get; set; } = 50;
    public float SustainSec { get; set; } = 3.0f;
    public float SpectralCentroidHz { get; set; } = 1100f;
    public float LowBandDb { get; set; }
    public float LowMidBandDb { get; set; }
    public float MidBandDb { get; set; }
    public float HighMidBandDb { get; set; }
    public float TrebleBandDb { get; set; }
    public string ToneRecommendation { get; set; } = "";
    public List<GuitarNoteMeasurementDto> Notes { get; set; } = new();

    public GuitarProfileDto Clone() => new()
    {
        Id = Id,
        Name = Name,
        CreatedAt = CreatedAt,
        EstimatedPickupType = EstimatedPickupType,
        NoiseFloorDb = NoiseFloorDb,
        Hum60HzDb = Hum60HzDb,
        SuggestedGateDb = SuggestedGateDb,
        PeakDb = PeakDb,
        RmsDb = RmsDb,
        CrestFactorDb = CrestFactorDb,
        SuggestedInputTrimDb = SuggestedInputTrimDb,
        BrightnessScore = BrightnessScore,
        WarmthScore = WarmthScore,
        OutputPowerScore = OutputPowerScore,
        SustainSec = SustainSec,
        SpectralCentroidHz = SpectralCentroidHz,
        LowBandDb = LowBandDb,
        LowMidBandDb = LowMidBandDb,
        MidBandDb = MidBandDb,
        HighMidBandDb = HighMidBandDb,
        TrebleBandDb = TrebleBandDb,
        ToneRecommendation = ToneRecommendation,
        Notes = (Notes ?? new List<GuitarNoteMeasurementDto>()).Select(n => n.Clone()).ToList(),
    };
}

public sealed class GuitarAnalysisLiveStateDto
{
    public bool Active { get; set; }
    public bool Completed { get; set; }
    public int StepIndex { get; set; }
    public int TotalSteps { get; set; }
    public string StepId { get; set; } = "";
    public string StepTitle { get; set; } = "";
    public string Instruction { get; set; } = "";
    public string TargetNote { get; set; } = "";
    public float TargetFreqHz { get; set; }
    public float ProgressPct { get; set; }
    public bool IsMatched { get; set; }
    public string LiveNote { get; set; } = "--";
    public float LiveFreqHz { get; set; }
    public float LiveCents { get; set; }
    public float LiveLevelDb { get; set; } = -90f;
    public float LivePeakDb { get; set; } = -96f;
    public string StatusHint { get; set; } = "";
    public GuitarProfileDto? DraftProfile { get; set; }
}
