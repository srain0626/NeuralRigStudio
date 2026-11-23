using System.Text.Json;
using NeuralRigStudio.Audio;

namespace NeuralRigStudio.Services;

public sealed record ToneDesignResult(
    bool Ok,
    string BackendUsed,
    string PresetName,
    string Summary,
    string[] GearHighlights,
    SignalChainConfigDto Chain);

/// <summary>
/// NeuralRig Studio 72종 실제 회로도 디바이스 카탈로그·검증기·시맨틱 톤 아키텍트.
/// Antigravity MCP 어댑터(NeuralRigMcp) 및 C# 오디오 브리지(RigWebBridge)가 공용으로 사용한다.
/// </summary>
public sealed class AntigravityToneArchitect
{

    public sealed record DeviceMeta(
        string Id,
        string Name,
        string Category, // amp, nam, cab, drive, delay, reverb, mod, eq, dyn
        float[] Defaults,
        (float Min, float Max)[] Ranges,
        string Hint);

    private static readonly (float Min, float Max)[] AmpRanges =
    {
        (0, 10), (0, 10), (0, 10), (0, 10), (0, 10), (0, 10), (0, 10), (0, 1)
    };

    private static readonly (float Min, float Max)[] CabRanges =
    {
        (20, 350), (2000, 16000), (-12, 12)
    };

    private static readonly (float Min, float Max)[] Drive3Ranges =
    {
        (0, 10), (0, 10), (0, 10)
    };

    private static readonly (float Min, float Max)[] EQ3AmpPedalRanges =
    {
        (0, 10), (0, 10), (0, 10), (0, 10), (0, 10)
    };

    public static readonly Dictionary<string, DeviceMeta> Catalog = BuildCatalog();

    private static Dictionary<string, DeviceMeta> BuildCatalog()
    {
        var list = new DeviceMeta[]
        {
            // ── AMP (11) ──
            new("amp_plexi", "Brit Plexi 100 Bright", "amp", new float[]{6.2f,5.2f,5.8f,6.0f,5.5f,3.5f,5.2f,1f}, AmpRanges, "Marshall 1959SLP Plexi (P0=Gain,P1=Bass,P2=Mid,P3=Treble,P4=Master,P5=Sag,P6=Presence,P7=Bright)"),
            new("amp_2203", "Marshall JCM800 (2203)", "amp", new float[]{6.8f,5.0f,6.2f,5.8f,5.5f,3.5f,5.2f,0f}, AmpRanges, "JCM800 2203 Hard Rock"),
            new("amp_twin", "US Clean Twin", "amp", new float[]{3.2f,5.0f,4.5f,5.8f,5.5f,3.5f,5.2f,0f}, AmpRanges, "Fender Twin Reverb Blackface Clean"),
            new("amp_1star", "CA 1Star Clean 100W", "amp", new float[]{3.8f,5.0f,5.5f,5.8f,5.5f,4.5f,5.2f,0f}, AmpRanges, "Boutique California Clean/Crunch"),
            new("amp_slo100", "SLO 100 Lead", "amp", new float[]{6.5f,5.0f,5.5f,5.8f,5.5f,3.5f,6.0f,0f}, AmpRanges, "Soldano SLO-100 Singing High-Gain"),
            new("amp_5153", "5153 Red High-Gain", "amp", new float[]{7.2f,5.0f,4.6f,5.8f,5.5f,3.5f,5.2f,1f}, AmpRanges, "EVH 5150 III Red Modern Metal"),
            new("amp_jc120", "Roland JC-120 Jazz Chorus", "amp", new float[]{4.5f,4.8f,5.2f,6.2f,5.5f,0f,0f,1f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,10f),(0f,10f),(0f,0f),(0f,0f),(0f,1f)}, "Roland JC-120 Stereo Solid-State Clean (P0=Vol,P1=Bass,P2=Mid,P3=Treble,P4=Master,P7=Bright)"),
            new("amp_ac30", "VOX AC30 Top Boost", "amp", new float[]{5.4f,5.0f,5.5f,6.6f,5.5f,3.5f,5.2f,1f}, AmpRanges, "VOX AC30 Top Boost British Chime"),
            new("amp_dsl", "Marshall JCM2000 DSL", "amp", new float[]{6.6f,5.0f,6.0f,5.8f,5.5f,3.5f,6.2f,0f}, AmpRanges, "Marshall JCM2000 DSL J-Rock Crunch/Lead"),
            new("amp_recto", "Mesa/Boogie Dual Rectifier", "amp", new float[]{7.4f,5.0f,4.4f,5.8f,5.5f,3.5f,5.2f,1f}, AmpRanges, "Mesa Dual Rectifier Aggressive Metal"),
            new("amp_be100", "Friedman BE-100", "amp", new float[]{6.9f,5.0f,6.4f,5.8f,5.5f,3.5f,6.0f,0f}, AmpRanges, "Friedman BE-100 Boutique Hot-Rodded Plexi"),

            // ── NAM (1) ──
            new("nam_capture", "NAM Neural Capture", "nam", new float[]{0f,0f,100f}, new[]{(-12f,12f),(-12f,12f),(0f,100f)}, "Neural Amp Modeler Block (P0=In dB,P1=Out dB,P2=Mix%)"),

            // ── CAB (8) ──
            new("cab_v30", "4x12 V30 Modern", "cab", new float[]{80f,9500f,0f}, CabRanges, "4x12 Celestion V30 (P0=LowCut Hz,P1=HighCut Hz,P2=Level dB)"),
            new("cab_custom", "Custom WAV IR", "cab", new float[]{75f,10500f,0f}, CabRanges, "Custom External WAV Cabinet IR"),
            new("cab_jc120", "2x12 JC-120 Open Back", "cab", new float[]{65f,12500f,0f}, CabRanges, "2x12 JC-120 Bright Clean Open Back"),
            new("cab_alnico", "2x12 VOX Alnico Blue", "cab", new float[]{75f,11000f,0f}, CabRanges, "2x12 VOX Alnico Blue"),
            new("cab_1960a", "Marshall 1960A (G12T-75)", "cab", new float[]{85f,9800f,0f}, CabRanges, "Marshall 1960A 4x12 G12T-75"),
            new("cab_1960bv", "Marshall 1960BV (4x12 V30)", "cab", new float[]{80f,9600f,0f}, CabRanges, "Marshall 1960BV Straight V30"),
            new("cab_mesa412", "Mesa/Boogie Rectifier 4x12", "cab", new float[]{90f,9200f,0f}, CabRanges, "Mesa Oversized Rectifier 4x12"),
            new("cab_jensen112", "1x12 Fender Jensen Open", "cab", new float[]{70f,11800f,0f}, CabRanges, "1x12 Fender Jensen Open Back"),

            // ── DRIVE / DIST / FUZZ / BOOST (25) ──
            new("od_ts808", "Green 808 (TS808)", "drive", new float[]{3.5f,5.8f,6.2f}, Drive3Ranges, "Ibanez TS808 (P0=Drive,P1=Tone,P2=Level)"),
            new("od_ts9", "Ibanez TS9 Tube Screamer", "drive", new float[]{3.8f,6.0f,6.4f}, Drive3Ranges, "Ibanez TS9 (P0=Drive,P1=Tone,P2=Level)"),
            new("od_janray", "Vemuram Jan Ray", "drive", new float[]{4.5f,5.2f,6.0f,4.8f,5.8f}, EQ3AmpPedalRanges, "Jan Ray (P0=Gain,P1=Bass,P2=Treble,P3=Sat,P4=Vol)"),
            new("od_puresky", "Caline Pure Sky", "drive", new float[]{4.0f,5.0f,5.6f,5.8f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,10f)}, "Timmy-style Transparent OD (P0=Gain,P1=Bass,P2=Treble,P3=Vol)"),
            new("od_bbpreamp", "Xotic BB Preamp", "drive", new float[]{5.2f,5.0f,5.8f,5.6f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,10f)}, "Xotic BB Preamp (P0=Gain,P1=Bass,P2=Treble,P3=Vol)"),
            new("od_ocd", "Fulltone OCD", "drive", new float[]{5.4f,5.6f,5.5f,1f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,1f)}, "Fulltone OCD MOSFET (P0=Drive,P1=Tone,P2=Vol,P3=HP1/LP0)"),
            new("od_bluesbreaker", "Marshall Bluesbreaker MK1", "drive", new float[]{5.2f,5.8f,6.0f}, Drive3Ranges, "Marshall Bluesbreaker MK1 Low-Gain OD"),
            new("od_boxofrock", "ZVEX Box of Rock (JTM45)", "drive", new float[]{6.0f,5.6f,5.5f}, Drive3Ranges, "Box of Rock BS170 JTM45 Emulator"),
            new("dist_rat2", "ProCo RAT 2", "drive", new float[]{6.2f,4.4f,5.4f}, Drive3Ranges, "ProCo RAT 2 (P0=Dist,P1=Reverse Filter,P2=Vol)"),
            new("dist_ecstasy", "Bogner Ecstasy Red", "drive", new float[]{6.8f,5.2f,5.8f,6.0f,5.4f}, EQ3AmpPedalRanges, "Bogner Ecstasy Red High-Gain (P0=Gain,P1=Bass,P2=Mid,P3=Treble,P4=Vol)"),
            new("dist_guvnor", "Marshall Guv'nor MK1", "drive", new float[]{6.4f,5.5f,5.6f,5.8f,5.4f}, EQ3AmpPedalRanges, "Marshall Guv'nor LED Clipping (P0=Gain,P1=Bass,P2=Mid,P3=Treble,P4=Level)"),
            new("dist_plus", "MXR Distortion+", "drive", new float[]{6.6f,5.8f}, new[]{(0f,10f),(0f,10f)}, "MXR Distortion+ Ge Hard Clip (P0=Dist,P1=Output)"),
            new("boost_microamp", "MXR Micro Amp", "drive", new float[]{4.5f}, new[]{(0f,10f)}, "MXR Micro Amp Clean Boost (P0=Gain)"),
            new("boost_rangemaster", "Dallas Rangemaster (OC44)", "drive", new float[]{6.5f,0f}, new[]{(0f,10f),(0f,1f)}, "Dallas Rangemaster Ge Treble Booster (P0=Boost,P1=0:4n7/1:22n)"),
            new("od_klon", "Mythic Centaur (Klon)", "drive", new float[]{4.2f,5.8f,5.5f}, Drive3Ranges, "Klon Centaur (P0=Gain,P1=Treble,P2=Output)"),
            new("dist_rat", "Rodent Dist (RAT)", "drive", new float[]{5.8f,4.5f,5.2f}, Drive3Ranges, "Classic LM308 RAT"),
            new("fuzz_bigmuff", "Big Pi Fuzz (Muff)", "drive", new float[]{6.5f,5.2f,5.0f}, Drive3Ranges, "EHX Big Muff Pi (P0=Sustain,P1=Tone,P2=Vol)"),
            new("fuzz_face", "Dallas Fuzz Face (Ge/Si)", "drive", new float[]{7.5f,5.0f,5.6f,0f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,1f)}, "Fuzz Face (P0=Fuzz,P1=Bias,P2=Vol,P3=0:Ge/1:Si)"),
            new("od_sd1", "BOSS SD-1 Super OverDrive", "drive", new float[]{4.5f,5.5f,5.8f}, Drive3Ranges, "BOSS SD-1 Asymmetric Overdrive"),
            new("od_bd2", "BOSS BD-2 Blues Driver", "drive", new float[]{4.6f,5.4f,5.6f}, Drive3Ranges, "BOSS BD-2 JFET Blues Driver"),
            new("dist_ds1", "BOSS DS-1 Distortion", "drive", new float[]{6.2f,4.2f,5.5f}, Drive3Ranges, "BOSS DS-1 Distortion"),
            new("dist_mt2", "BOSS MT-2 Metal Zone", "drive", new float[]{7.2f,2.5f,3.0f,-4.0f,950f,5.2f}, new[]{(0f,10f),(-15f,15f),(-15f,15f),(-15f,15f),(200f,5000f),(0f,10f)}, "BOSS MT-2 (P0=Dist,P1=Hi,P2=Lo,P3=Mid,P4=MidFreq,P5=Lvl)"),
            new("dist_hm2", "BOSS HM-2 Heavy Metal", "drive", new float[]{8.5f,8.5f,9.0f,5.5f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,10f)}, "BOSS HM-2 Chainsaw (P0=Dist,P1=Low,P2=High,P3=Level)"),
            new("boost_ep", "Xotic EP Booster", "drive", new float[]{4.0f,1f}, new[]{(0f,10f),(0f,1f)}, "Xotic EP Booster JFET (P0=Boost,P1=Bright)"),
            new("boost_rc", "Xotic RC Booster", "drive", new float[]{2.5f,6.0f,5.2f,5.6f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,10f)}, "Xotic RC Booster (P0=Gain,P1=Vol,P2=Bass,P3=Treble)"),

            // ── DELAY (5) ──
            new("dly_dm2w", "BOSS DM-2W Delay", "delay", new float[]{340f,38f,28f,1f}, new[]{(40f,800f),(0f,85f),(0f,100f),(0f,1f)}, "BOSS DM-2W BBD Delay (P0=ms,P1=FB%,P2=Mix%,P3=Custom)"),
            new("dly_dd3t", "BOSS DD-3T Digital Delay", "delay", new float[]{380f,34f,26f}, new[]{(20f,800f),(0f,85f),(0f,100f)}, "BOSS DD-3T Digital Delay"),
            new("dly_analog", "Analog BBD Delay", "delay", new float[]{360f,36f,48f,26f}, new[]{(40f,1200f),(0f,85f),(0f,100f),(0f,100f)}, "Analog BBD Delay (P0=ms,P1=FB%,P2=Tone%,P3=Mix%)"),
            new("dly_digital", "Digital Stereo Delay", "delay", new float[]{420f,34f,18f,24f}, new[]{(40f,1350f),(0f,85f),(0f,100f),(0f,100f)}, "Digital Stereo Ping-Pong Delay"),
            new("dly_timeline", "Strymon TimeLine", "delay", new float[]{430f,36f,28f,26f,0f}, new[]{(40f,1350f),(0f,85f),(0f,100f),(0f,100f),(0f,1f)}, "Strymon TimeLine (P0=ms,P1=Rep%,P2=Mod/Filter%,P3=Mix%,P4=0:dBucket/1:dTape)"),

            // ── REVERB (5) ──
            new("rev_bluesky", "Strymon blueSky", "reverb", new float[]{66f,22f,36f,32f,28f}, new[]{(5f,100f),(0f,120f),(0f,100f),(0f,100f),(0f,100f)}, "Strymon blueSky (P0=Decay%,P1=PreDly,P2=Damp%,P3=Mix%,P4=Shimmer%)"),
            new("rev_rv6", "BOSS RV-6 Reverb", "reverb", new float[]{28f,58f,56f,0f}, new[]{(0f,100f),(0f,100f),(5f,100f),(0f,3f)}, "BOSS RV-6 (P0=Mix%,P1=Tone%,P2=Time%,P3=Mode 0:Hall 1:Plate 2:Spr 3:Shim)"),
            new("rev_spring", "Spring Reverb", "reverb", new float[]{48f,42f,25f}, new[]{(5f,100f),(0f,100f),(0f,100f)}, "Classic Combo Spring Reverb"),
            new("rev_hall", "Hall Ambient Reverb", "reverb", new float[]{58f,45f,24f,26f}, new[]{(5f,100f),(0f,100f),(0f,120f),(0f,100f)}, "Studio Hall Reverb"),
            new("rev_plate", "Plate Studio Reverb", "reverb", new float[]{52f,32f,22f}, new[]{(5f,100f),(0f,100f),(0f,100f)}, "Studio Plate Reverb"),

            // ── MODULATION (7) ──
            new("mod_ce2", "BOSS CE-2 Chorus", "mod", new float[]{1.1f,48f,45f}, new[]{(0.1f,5.5f),(0f,100f),(0f,100f)}, "BOSS CE-2 Stereo Chorus (P0=Hz,P1=Depth%,P2=Mix%)"),
            new("mod_dc2w", "BOSS DC-2W Dimension C", "mod", new float[]{2f,60f}, new[]{(1f,4f),(0f,100f)}, "BOSS DC-2W Dimension C (P0=Mode 1..4,P1=Mix%)"),
            new("mod_bf2", "BOSS BF-2 Flanger", "mod", new float[]{4.5f,55f,0.65f,48f}, new[]{(0f,10f),(0f,100f),(0.1f,5.5f),(0f,85f)}, "BOSS BF-2 Flanger"),
            new("mod_phase90", "Phase 90 Phaser", "mod", new float[]{0.8f,65f,35f,50f}, new[]{(0.1f,5.5f),(0f,100f),(0f,80f),(0f,100f)}, "MXR Phase 90 Phaser"),
            new("mod_tremolo", "Optical Tremolo", "mod", new float[]{4.5f,55f,0f}, new[]{(0.5f,12f),(0f,100f),(0f,1f)}, "Optical CdS Tremolo"),
            new("mod_smallclone", "EHX Small Clone Chorus", "mod", new float[]{0.85f,1f,52f}, new[]{(0.1f,5.5f),(0f,1f),(0f,100f)}, "EHX Small Clone BBD Chorus"),
            new("mod_univibe", "Shin-ei Uni-Vibe (1968)", "mod", new float[]{1.6f,72f,0f}, new[]{(0.2f,8.0f),(0f,100f),(0f,1f)}, "Shin-ei Uni-Vibe Optical 4-Stage (P0=Hz,P1=Intensity%,P2=0:Chorus/1:Vibrato)"),

            // ── EQ / WAH (4) ──
            new("eq_crybaby", "Dunlop Cry Baby GCB-95 Wah", "eq", new float[]{52f,35f,5.8f,100f}, new[]{(0f,100f),(0f,100f),(0f,10f),(0f,100f)}, "Cry Baby Wah (P0=Pedal%,P1=AutoEnv%,P2=Q,P3=Mix%)"),
            new("eq_ge7", "BOSS GE-7 Graphic EQ", "eq", new float[]{0f,0f,0f,0f,0f,0f,0f,0f}, new[]{(-15f,15f),(-15f,15f),(-15f,15f),(-15f,15f),(-15f,15f),(-15f,15f),(-15f,15f),(-15f,15f)}, "BOSS GE-7 7-Band EQ (100..6.4k + Level dB)"),
            new("eq_graphic5", "Graphic 5-Band EQ", "eq", new float[]{0f,0f,0f,0f,0f,0f}, new[]{(-12f,12f),(-12f,12f),(-12f,12f),(-12f,12f),(-12f,12f),(-12f,12f)}, "5-Band Graphic EQ"),
            new("eq_parametric", "Parametric EQ", "eq", new float[]{70f,900f,2.0f,1.4f,11000f}, new[]{(20f,350f),(150f,5000f),(-12f,12f),(0.4f,6.0f),(2500f,16000f)}, "Parametric EQ (LowCut,MidHz,MidDb,Q,HiCut)"),

            // ── DYNAMICS (6) ──
            new("gate_adaptive", "Adaptive Noise Gate", "dyn", new float[]{-56f,120f,80f}, new[]{(-80f,-20f),(20f,400f),(10f,300f)}, "Adaptive Noise Gate"),
            new("comp_sp", "Xotic SP Compressor", "dyn", new float[]{55f,5.4f,1f}, new[]{(0f,100f),(0f,10f),(0f,2f)}, "Xotic SP Comp (P0=Blend%,P1=Vol,P2=0:Lo/1:Mid/2:Hi)"),
            new("comp_orangesqueezer", "Dan Armstrong Orange Squeezer", "dyn", new float[]{5.6f,5.4f,5.5f,100f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,100f)}, "Orange Squeezer Half-Wave JFET Comp"),
            new("comp_cs3", "BOSS CS-3 Comp Sustainer", "dyn", new float[]{5.5f,5.5f,5.0f,5.2f}, new[]{(0f,10f),(0f,10f),(0f,10f),(0f,10f)}, "BOSS CS-3 Comp"),
            new("comp_dyna", "Dyna Comp", "dyn", new float[]{4.5f,5.5f,12f,100f}, new[]{(0f,10f),(0f,10f),(1f,50f),(0f,100f)}, "MXR Dyna Comp"),
            new("comp_1176", "1176 Studio Comp", "dyn", new float[]{5.0f,5.2f,4f,85f}, new[]{(0f,10f),(0f,10f),(2f,20f),(0f,100f)}, "1176 Studio FET Comp"),
        };

        var dict = new Dictionary<string, DeviceMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in list)
            dict[d.Id] = d;
        return dict;
    }

    // =========================================================================
    // Semantic Korean/English Guitar Circuit Tone Architect + Slot/Scene Ops
    // =========================================================================
    public ToneDesignResult GenerateViaBuiltinArchitect(
        string prompt,
        SignalChainConfigDto? currentChain = null,
        GuitarProfileDto? guitarProfile = null)
    {
        string low = prompt.ToLowerInvariant();
        bool isRelative = currentChain != null && ContainsAny(low,
            "현재", "지금", "기존", "이 톤", "더 올려", "올려", "줄여", "낮춰", "빼줘", "제거", "추가", "바꿔", "정리");

        SignalChainConfigDto dto;
        string summary;

        if (isRelative && currentChain != null)
        {
            dto = currentChain.Clone();
            dto.PresetId = $"AI{DateTime.Now:HHmm}".Substring(0, 6);
            summary = ApplyRelativeEdits(dto, prompt, low);
            ApplyTempoModifiers(dto, low);
            if (guitarProfile != null && ContainsAny(low, "내 기타", "프로필", "보정", "싱글", "험버커"))
            {
                string gNote = ApplyGuitarProfileCalibration(dto, guitarProfile);
                if (!string.IsNullOrEmpty(gNote)) summary += $" · {gNote}";
            }
        }
        else
        {
            (dto, summary) = BuildArchetypeFromPrompt(prompt, low);
            ApplyExplicitGearOverrides(dto, low);
            ApplyModifierTones(dto, low);
            ApplyTempoModifiers(dto, low);
            if (guitarProfile != null)
            {
                string gNote = ApplyGuitarProfileCalibration(dto, guitarProfile);
                if (!string.IsNullOrEmpty(gNote)) summary += $" · {gNote}";
            }
        }

        var normalized = ValidateAndNormalizeChain(dto, InferShortPresetTitle(prompt));
        return new ToneDesignResult(
            true,
            "NeuralRig Circuit Architect",
            normalized.PresetName,
            summary,
            ExtractGearHighlights(normalized),
            normalized);
    }

    private static void ApplyTempoModifiers(SignalChainConfigDto dto, string low)
    {
        var m = System.Text.RegularExpressions.Regex.Match(low, @"(\d{2,3}(?:\.\d+)?)\s*bpm");
        if (m.Success && float.TryParse(m.Groups[1].Value, out float bpm))
            dto.TempoBpm = Math.Clamp(bpm, 40f, 280f);

        int syncDiv = 0;
        if (ContainsAny(low, "점8분", "점팔분", "dotted 8", "dotted eighth", "1/8.", "1/8d")) syncDiv = 2;
        else if (ContainsAny(low, "셋잇단", "트립플렛", "triplet", "1/8t")) syncDiv = 4;
        else if (ContainsAny(low, "16분", "1/16", "sixteenth")) syncDiv = 5;
        else if (ContainsAny(low, "8분", "1/8", "eighth")) syncDiv = 3;
        else if (ContainsAny(low, "4분", "정박", "1/4", "quarter")) syncDiv = 1;

        if (syncDiv > 0)
        {
            foreach (var s in dto.Slots)
            {
                if (!string.IsNullOrEmpty(s.ModelId) && Catalog.TryGetValue(s.ModelId, out var meta) && meta.Category == "delay")
                    s.SyncDivision = syncDiv;
            }
        }
    }

    private static string ApplyGuitarProfileCalibration(SignalChainConfigDto dto, GuitarProfileDto g)
    {
        dto.InputGainDb = MathF.Round(Math.Clamp(g.SuggestedInputTrimDb, -12f, 12f), 1);

        bool hasHighGain = dto.Slots.Any(s => s.Enabled && s.ModelId is "amp_recto" or "amp_5153" or "amp_slo100" or "dist_mt2" or "dist_hm2");
        float targetGate = MathF.Round(Math.Clamp(g.SuggestedGateDb + (hasHighGain ? 3.0f : 0f), -78f, -18f), 1);

        foreach (var sl in dto.Slots)
        {
            if (string.Equals(sl.ModelId, "gate_adaptive", StringComparison.OrdinalIgnoreCase))
            {
                sl.Enabled = true;
                sl.Params[0] = targetGate;
            }
            else if (!string.IsNullOrEmpty(sl.ModelId) && Catalog.TryGetValue(sl.ModelId, out var meta) && meta.Category == "amp" && sl.ModelId != "amp_jc120")
            {
                bool isSingleCoil = g.BrightnessScore >= 64 || g.EstimatedPickupType.Contains("Single", StringComparison.OrdinalIgnoreCase);
                bool isDarkHum = g.BrightnessScore <= 42 || (g.EstimatedPickupType.Contains("Humbucker", StringComparison.OrdinalIgnoreCase) && g.BrightnessScore < 50);

                if (isSingleCoil && hasHighGain)
                {
                    sl.Params[3] = Math.Clamp(sl.Params[3] - 0.6f, 0f, 10f); // Treble 완화
                    sl.Params[1] = Math.Clamp(sl.Params[1] + 0.4f, 0f, 10f); // Bass 보강
                }
                else if (isDarkHum && !hasHighGain)
                {
                    sl.Params[3] = Math.Clamp(sl.Params[3] + 0.6f, 0f, 10f); // Treble 보강
                    sl.Params[1] = Math.Clamp(sl.Params[1] - 0.4f, 0f, 10f); // Low-end 타이트닝
                    sl.Params[7] = 1f;                                        // Bright 활성화
                }
            }
        }

        return $"기타 맞춤 보정({g.Name}: Trim {dto.InputGainDb:+0.0;-0.0;0.0}dB, Gate {targetGate:F0}dB)";
    }

    private static (SignalChainConfigDto dto, string summary) BuildArchetypeFromPrompt(string prompt, string low)
    {
        string presetName = InferShortPresetTitle(prompt);

        // 1) Hendrix / 60s Psychedelic Fuzz & Uni-Vibe
        if (ContainsAny(low, "헨드릭스", "hendrix", "퍼즈페이스", "fuzz face", "유니바이브", "univibe", "사이키델릭", "voodoo"))
        {
            var c = CreateBaseChain(presetName, routingMode: 0);
            SetSlot(c, 0, "eq_crybaby", true, 54f, 40f, 6.2f, 100f);
            SetSlot(c, 1, "fuzz_face", true, 8.2f, 4.9f, 5.8f, 0f);
            SetSlot(c, 2, "amp_plexi", true, 6.2f, 5.4f, 6.4f, 6.0f, 6.4f, 4.8f, 5.6f, 1f);
            SetSlot(c, 3, "cab_1960bv", true, 80f, 9600f, 0f);
            SetSlot(c, 4, "mod_univibe", true, 1.8f, 74f, 0f);
            SetSlot(c, 6, "rev_spring", true, 52f, 42f, 24f);
            return (c, "60s 빈티지 퍼즈 사운드: Cry Baby Wah → Ge AC128 Fuzz Face → Plexi 100 → 1960BV V30 → Shin-ei Uni-Vibe 4단 페이즈 스로브 조합");
        }

        // 2) John Mayer / SRV / Texas Blues / Bluesbreaker
        if (ContainsAny(low, "메이어", "mayer", "srv", "스티비", "블루스", "blues", "텍사스", "bluesbreaker", "블루스브레이커", "오렌지 스퀴저", "orange squeezer", "존 메이어"))
        {
            var c = CreateBaseChain(presetName, routingMode: 0);
            SetSlot(c, 0, "comp_orangesqueezer", true, 5.8f, 5.6f, 5.2f, 92f);
            SetSlot(c, 1, "od_ts808", true, 3.2f, 5.8f, 6.4f);
            SetSlot(c, 2, "od_bluesbreaker", true, 4.8f, 5.8f, 6.0f);
            SetSlot(c, 3, "amp_twin", true, 4.2f, 5.6f, 5.4f, 6.2f, 5.8f, 4.0f, 5.4f, 1f);
            SetSlot(c, 4, "cab_jensen112", true, 70f, 11400f, 0f);
            SetSlot(c, 6, "rev_spring", true, 54f, 40f, 26f);
            return (c, "네오 블루스·클린 브레이크업: Orange Squeezer + TS808/Bluesbreaker 스택 → US Clean Twin → 1x12 Jensen 오픈백 → Spring 리버브");
        }

        // 3) J-Pop Clean / Sparkle / Yorushika / City Pop / Funky Clean
        if (ContainsAny(low, "jpop", "j-pop", "제이팝", "요루시카", "yorushika", "시티팝", "city pop", "네오소울", "neo soul", "요아소비", "yoasobi", "스파클", "클린", "clean", "재즈코러스", "jc120", "잔레이", "jan ray"))
        {
            var c = CreateBaseChain(presetName, routingMode: 0);
            SetSlot(c, 0, "comp_sp", true, 56f, 5.6f, 1f);
            SetSlot(c, 1, "od_janray", true, 3.4f, 5.0f, 6.2f, 4.2f, 5.6f);
            SetSlot(c, 2, "amp_jc120", true, 4.6f, 4.8f, 5.4f, 6.4f, 5.5f, 0f, 0f, 1f);
            SetSlot(c, 3, "cab_jc120", true, 65f, 12500f, 0f);
            SetSlot(c, 4, "mod_dc2w", true, 2f, 58f);
            SetSlot(c, 5, "dly_dd3t", true, 360f, 30f, 22f);
            SetSlot(c, 6, "rev_bluesky", true, 64f, 22f, 34f, 28f, 24f);
            return (c, "영롱한 J-Pop 스튜디오 클린: Xotic SP Comp → Jan Ray → 스테레오 Roland JC-120 + BOSS DC-2W Dimension C → Strymon blueSky");
        }

        // 4) J-Rock / Bocchi / Anisong Crunch & Lead
        if (ContainsAny(low, "jrock", "j-rock", "제이락", "봇치", "결속밴드", "bocchi", "애니송", "anisong", "기타히어로", "크런치", "crunch", "아시안", "원오크락", "one ok"))
        {
            bool leadFocus = ContainsAny(low, "솔로", "solo", "리드", "lead", "드라이브", "강한");
            var c = CreateBaseChain(presetName, routingMode: 0);
            SetSlot(c, 0, "gate_adaptive", true, -56f, 95f, 60f);
            SetSlot(c, 1, leadFocus ? "dist_rat2" : "od_ts9", true, leadFocus ? 5.6f : 3.8f, leadFocus ? 4.6f : 6.0f, 6.2f);
            SetSlot(c, 2, leadFocus ? "amp_dsl" : "amp_ac30", true, leadFocus ? 6.6f : 5.6f, 5.0f, 6.2f, 6.4f, 5.8f, 3.8f, 6.0f, 1f);
            SetSlot(c, 3, leadFocus ? "cab_1960a" : "cab_alnico", true, 80f, 10200f, 0f);
            SetSlot(c, 4, "eq_ge7", true, 1.0f, 0f, -1.0f, 1.5f, 2.0f, 1.5f, 0f, 0f);
            SetSlot(c, 5, "dly_timeline", true, 390f, 32f, 24f, 24f, 0f);
            SetSlot(c, 6, "rev_bluesky", true, 54f, 18f, 38f, 22f, 0f);
            return (c, "J-Rock / 애니송 펀치 리드·크런치: Adaptive Gate → RAT2/TS9 → VOX AC30 또는 JCM2000 DSL → TimeLine + blueSky");
        }

        // 5) Modern Metal / Djent / 7-String / Rectifier / 5153
        if (ContainsAny(low, "메탈", "metal", "젠트", "djent", "헤비", "heavy", "메탈존", "하이게인", "high gain", "high-gain", "렉티", "recto", "5150", "5153", "하드코어", "메탈코어", "스웨덴", "체인소"))
        {
            bool chainsaw = ContainsAny(low, "스웨덴", "체인소", "hm2", "hm-2", "데스메탈");
            var c = CreateBaseChain(presetName, routingMode: 0);
            SetSlot(c, 0, "gate_adaptive", true, -48f, 75f, 45f);
            SetSlot(c, 1, chainsaw ? "dist_hm2" : "od_ts808", true, chainsaw ? 8.8f : 1.4f, chainsaw ? 8.8f : 6.6f, chainsaw ? 9.0f : 7.8f, chainsaw ? 5.6f : 0f);
            SetSlot(c, 2, chainsaw ? "amp_2203" : "amp_recto", true, 7.2f, 5.0f, chainsaw ? 6.0f : 4.6f, 6.4f, 5.4f, 2.2f, 6.2f, 1f);
            SetSlot(c, 3, "cab_mesa412", true, 95f, 9000f, 0f);
            SetSlot(c, 4, "eq_ge7", true, 2.0f, 0.5f, -2.5f, 1.5f, 2.5f, 1.5f, 0.5f, 0f);
            SetSlot(c, 6, "rev_plate", true, 36f, 38f, 14f);
            return (c, "타이트한 모던 하이게인 메탈: 노이즈 게이트(-48dB) → TS808 클린 부스트 → Dual Rectifier → Mesa 4x12 캐비넷 + GE-7 미드 스쿱");
        }

        // 6) Ambient / Shimmer / Post-Rock / Worship / Shoegaze
        if (ContainsAny(low, "앰비언트", "ambient", "쉬머", "shimmer", "워십", "worship", "포스트락", "post rock", "슈게이즈", "shoegaze", "몽환", "공간", "드림팝"))
        {
            bool shoegaze = ContainsAny(low, "슈게이즈", "shoegaze", "빅머프", "big muff");
            var c = CreateBaseChain(presetName, routingMode: 1, splitCol: 1, mergeCol: 5, parallelMix: 0.52f);
            SetSlot(c, 0, "comp_1176", true, 5.0f, 5.4f, 4f, 82f);
            SetSlot(c, 1, shoegaze ? "fuzz_bigmuff" : "od_klon", true, shoegaze ? 6.8f : 4.2f, 5.6f, 5.6f);
            SetSlot(c, 2, "amp_1star", true, 3.8f, 5.2f, 5.4f, 6.2f, 5.6f, 4.2f, 5.8f, 1f);
            SetSlot(c, 3, "cab_v30", true, 70f, 11800f, 0f);
            SetSlot(c, 5, "dly_timeline", true, 480f, 46f, 34f, 36f, 1f);
            SetSlot(c, 6, "rev_bluesky", true, 84f, 28f, 30f, 44f, 48f);
            SetSlot(c, 9, "mod_smallclone", true, 0.75f, 1f, 54f);
            SetSlot(c, 10, "mod_tremolo", true, 3.6f, 36f, 0f);
            return (c, "입체 병렬 앰비언트 쉬머: 1176 Comp → Klon/BigMuff → CA 1Star + 병렬 Small Clone/Tremolo → TimeLine dTape → blueSky Shimmer(48%)");
        }

        // 7) Classic 70s/80s Hard Rock / JCM800 / Brian May / Solo Lead
        if (ContainsAny(low, "하드락", "hard rock", "클래식락", "classic rock", "jcm", "800", "브라이언", "queen", "레인지마스터", "rangemaster", "구브너", "guvnor", "솔로", "solo", "리드", "lead", "락"))
        {
            var c = CreateBaseChain(presetName, routingMode: 1, splitCol: 1, mergeCol: 5, parallelMix: 0.38f);
            SetSlot(c, 0, "gate_adaptive", true, -58f, 110f, 75f);
            SetSlot(c, 1, "od_sd1", true, 3.8f, 5.8f, 6.6f);
            SetSlot(c, 2, "amp_2203", true, 7.0f, 5.4f, 6.4f, 6.0f, 5.8f, 3.8f, 5.8f, 0f);
            SetSlot(c, 3, "cab_1960a", true, 85f, 9800f, 0f);
            SetSlot(c, 5, "dly_dm2w", true, 390f, 34f, 26f, 1f);
            SetSlot(c, 6, "rev_plate", true, 52f, 34f, 22f);
            SetSlot(c, 9, "eq_parametric", true, 80f, 1200f, 2.5f, 1.3f, 10500f);
            return (c, "80s 브리티시 하드락·솔로 리드: BOSS SD-1 비대칭 부스트 → JCM800 2203 → Marshall 1960A → DM-2W 아날로그 딜레이 + Plate 리버브");
        }

        // 8) Funk / Neo-Soul / Wah
        if (ContainsAny(low, "펑크", "funk", "와우", "wah", "크라이베이비", "crybaby", "오토와", "커팅", "cutting"))
        {
            var c = CreateBaseChain(presetName, routingMode: 0);
            SetSlot(c, 0, "eq_crybaby", true, 48f, 68f, 6.4f, 100f);
            SetSlot(c, 1, "comp_sp", true, 65f, 5.8f, 2f);
            SetSlot(c, 2, "boost_ep", true, 4.0f, 1f);
            SetSlot(c, 3, "amp_twin", true, 3.4f, 5.0f, 5.2f, 6.4f, 5.8f, 3.0f, 5.6f, 1f);
            SetSlot(c, 4, "cab_jensen112", true, 75f, 11800f, 0f);
            SetSlot(c, 6, "rev_spring", true, 42f, 45f, 20f);
            return (c, "펑키 커팅 & 오토 와우: Cry Baby Auto-Wah(68%) → SP Comp(Hi) → EP Booster → US Clean Twin → 1x12 Jensen");
        }

        // 9) Default balanced boutique rock/pop rig
        {
            var c = CreateBaseChain(presetName, routingMode: 1, splitCol: 1, mergeCol: 5, parallelMix: 0.40f);
            SetSlot(c, 0, "comp_sp", true, 48f, 5.4f, 1f);
            SetSlot(c, 1, "od_klon", true, 4.4f, 5.8f, 5.8f);
            SetSlot(c, 2, "amp_be100", true, 6.2f, 5.2f, 6.2f, 5.8f, 5.6f, 3.5f, 5.8f, 1f);
            SetSlot(c, 3, "cab_1960bv", true, 80f, 9600f, 0f);
            SetSlot(c, 5, "dly_digital", true, 410f, 32f, 20f, 24f);
            SetSlot(c, 6, "rev_hall", true, 56f, 42f, 22f, 26f);
            SetSlot(c, 9, "mod_ce2", true, 0.95f, 44f, 38f);
            return (c, "올라운드 부티크 리드/크런치: SP Comp → Klon Centaur → Friedman BE-100 → Marshall 1960BV V30 → 병렬 CE-2 Chorus + Hall 리버브");
        }
    }

    private static string ApplyRelativeEdits(SignalChainConfigDto dto, string rawPrompt, string low)
    {
        var actions = new List<string>();
        bool gainUp = ContainsAny(low, "게인 올려", "게인 더", "게인 높", "게인 +", "게인+", "더 세게", "더 쎄게", "더 강하게", "드라이브 더", "歪");
        bool gainDown = ContainsAny(low, "게인 줄", "게인 낮", "게인 빼", "클린하게", "클린 톤", "클린으로", "더 맑게", "약하게");
        bool wetUp = ContainsAny(low, "공간감 더", "공간감 추가", "리버브 더", "딜레이 더", "리버브 추가", "딜레이 추가", "딜레이/리버브", "촉촉", "깊게");
        bool wetDown = ContainsAny(low, "공간감 줄", "리버브 줄", "딜레이 줄", "드라이하게", "건조하게");
        bool brightUp = ContainsAny(low, "밝게", "트레블 올려", "고음", "까랑까랑", "선명");
        bool warmUp = ContainsAny(low, "따뜻", "어둡", "다크", "트레블 줄", "부드럽");

        if (ContainsAny(low, "병렬", "패럴렐", "parallel"))
        {
            dto.RoutingMode = 1;
            actions.Add("병렬(Parallel Split) 라우팅 전환");
        }
        if (ContainsAny(low, "딜레이 추가", "딜레이/리버브") && !dto.Slots.Any(s => !string.IsNullOrEmpty(s.ModelId) && Catalog.TryGetValue(s.ModelId, out var m) && m.Category == "delay"))
            ReplaceCategoryOrPlace(dto, "delay", "dly_digital", 5);
        if (ContainsAny(low, "리버브 추가", "딜레이/리버브") && !dto.Slots.Any(s => !string.IsNullOrEmpty(s.ModelId) && Catalog.TryGetValue(s.ModelId, out var m) && m.Category == "reverb"))
            ReplaceCategoryOrPlace(dto, "reverb", "rev_hall", 6);
        if (ContainsAny(low, "코러스", "chorus") && !ContainsAny(low, "스몰클론", "small clone", "디멘션", "dc2w", "dc-2w", "ce2", "ce-2"))
            ReplaceCategoryOrPlace(dto, "mod", "mod_ce2", dto.RoutingMode == 1 ? 9 : 4);

        foreach (var sl in dto.Slots)
        {
            if (string.IsNullOrEmpty(sl.ModelId) || !Catalog.TryGetValue(sl.ModelId, out var meta)) continue;
            if (meta.Category is "amp" or "drive")
            {
                if (gainUp) sl.Params[0] = Math.Clamp(sl.Params[0] + 1.8f, 0f, 10f);
                if (gainDown) sl.Params[0] = Math.Clamp(sl.Params[0] - 2.0f, 0f, 10f);
                if (meta.Category == "amp")
                {
                    if (brightUp) { sl.Params[3] = Math.Clamp(sl.Params[3] + 1.2f, 0f, 10f); sl.Params[7] = 1f; }
                    if (warmUp) { sl.Params[3] = Math.Clamp(sl.Params[3] - 1.2f, 0f, 10f); sl.Params[7] = 0f; }
                }
            }
            else if (meta.Category is "delay" or "reverb")
            {
                int mixIdx = sl.ModelId is "rev_bluesky" or "rev_hall" or "dly_analog" or "dly_digital" or "dly_timeline" ? 3
                           : sl.ModelId == "rev_rv6" ? 0 : 2;
                if (wetUp) { sl.Enabled = true; sl.Params[mixIdx] = Math.Clamp(sl.Params[mixIdx] + 14f, 0f, 85f); }
                if (wetDown) sl.Params[mixIdx] = Math.Clamp(sl.Params[mixIdx] - 12f, 0f, 85f);
            }
        }

        if (gainUp) actions.Add("드라이브/앰프 게인 증가");
        if (gainDown) actions.Add("드라이브/앰프 게인 감소");
        if (wetUp) actions.Add("공간계(딜레이/리버브) 믹스 확대");
        if (wetDown) actions.Add("공간계 믹스 축소");
        if (brightUp) actions.Add("고역/Bright 스위치 강화");
        if (warmUp) actions.Add("따뜻한 고역 톤으로 정리");

        int added = ApplyExplicitGearOverrides(dto, low);
        if (added > 0) actions.Add($"요청 장비 {added}종 반영");

        dto.PresetName = actions.Count > 0 ? $"{dto.PresetName} (AI)" : InferShortPresetTitle(rawPrompt);
        return actions.Count > 0
            ? $"현재 프리셋 기반 실시간 수정 완료: {string.Join(", ", actions)}"
            : "현재 시그널 체인의 밸런스를 유지하며 요청에 맞게 미세 조정했습니다.";
    }

    private static int ApplyExplicitGearOverrides(SignalChainConfigDto dto, string low)
    {
        int count = 0;

        // Drive pedals
        string? driveId = null;
        if (ContainsAny(low, "구브너", "guvnor", "guv'nor")) driveId = "dist_guvnor";
        else if (ContainsAny(low, "블루스브레이커", "bluesbreaker")) driveId = "od_bluesbreaker";
        else if (ContainsAny(low, "퍼즈페이스", "fuzz face")) driveId = "fuzz_face";
        else if (ContainsAny(low, "레인지마스터", "rangemaster", "트레블 부스트", "treble boost")) driveId = "boost_rangemaster";
        else if (ContainsAny(low, "디스토션 플러스", "distortion+", "dist+")) driveId = "dist_plus";
        else if (ContainsAny(low, "박스오브락", "box of rock")) driveId = "od_boxofrock";
        else if (ContainsAny(low, "클론", "센타우르", "klon", "centaur")) driveId = "od_klon";
        else if (ContainsAny(low, "잔레이", "jan ray")) driveId = "od_janray";
        else if (ContainsAny(low, "퓨어스카이", "pure sky")) driveId = "od_puresky";
        else if (ContainsAny(low, "랫", "rat2", "rat 2")) driveId = "dist_rat2";
        else if (ContainsAny(low, "빅머프", "big muff", "머프")) driveId = "fuzz_bigmuff";
        else if (ContainsAny(low, "메탈존", "mt2", "mt-2")) driveId = "dist_mt2";
        else if (ContainsAny(low, "블루스드라이버", "bd2", "bd-2")) driveId = "od_bd2";
        else if (ContainsAny(low, "ts9", "티에스9")) driveId = "od_ts9";
        else if (ContainsAny(low, "ts808", "튜브스크리머")) driveId = "od_ts808";
        else if (ContainsAny(low, "ocd")) driveId = "od_ocd";
        else if (ContainsAny(low, "bb preamp", "bb프리앰프")) driveId = "od_bbpreamp";

        if (driveId != null) { ReplaceCategoryOrPlace(dto, "drive", driveId, preferSlot: 1); count++; }

        // Amps
        string? ampId = null;
        if (ContainsAny(low, "jc120", "jc-120", "재즈코러스")) ampId = "amp_jc120";
        else if (ContainsAny(low, "렉티", "dual rectifier", "recto")) ampId = "amp_recto";
        else if (ContainsAny(low, "5153", "5150")) ampId = "amp_5153";
        else if (ContainsAny(low, "플렉시", "plexi")) ampId = "amp_plexi";
        else if (ContainsAny(low, "jcm800", "2203")) ampId = "amp_2203";
        else if (ContainsAny(low, "복스", "ac30")) ampId = "amp_ac30";
        else if (ContainsAny(low, "jcm2000", "dsl")) ampId = "amp_dsl";
        else if (ContainsAny(low, "프리드먼", "be100", "be-100")) ampId = "amp_be100";
        else if (ContainsAny(low, "트윈", "펜더", "fender", "twin")) ampId = "amp_twin";
        else if (ContainsAny(low, "slo100", "slo-100")) ampId = "amp_slo100";

        if (ampId != null) { ReplaceCategoryOrPlace(dto, "amp", ampId, preferSlot: 2); count++; }

        // Cabs
        string? cabId = null;
        if (ContainsAny(low, "1960a")) cabId = "cab_1960a";
        else if (ContainsAny(low, "1960bv")) cabId = "cab_1960bv";
        else if (ContainsAny(low, "mesa 4x12", "메사 캐비넷")) cabId = "cab_mesa412";
        else if (ContainsAny(low, "알니코", "alnico")) cabId = "cab_alnico";
        else if (ContainsAny(low, "젠센", "jensen")) cabId = "cab_jensen112";

        if (cabId != null) { ReplaceCategoryOrPlace(dto, "cab", cabId, preferSlot: 3); count++; }

        // Mod / Wah / Comp / Delay / Reverb
        if (ContainsAny(low, "유니바이브", "univibe", "uni-vibe")) { ReplaceCategoryOrPlace(dto, "mod", "mod_univibe", 4); count++; }
        else if (ContainsAny(low, "스몰클론", "small clone")) { ReplaceCategoryOrPlace(dto, "mod", "mod_smallclone", 4); count++; }
        else if (ContainsAny(low, "디멘션", "dc2w", "dc-2w")) { ReplaceCategoryOrPlace(dto, "mod", "mod_dc2w", 4); count++; }
        else if (ContainsAny(low, "페이즈90", "phase 90")) { ReplaceCategoryOrPlace(dto, "mod", "mod_phase90", 4); count++; }
        else if (ContainsAny(low, "트레몰로", "tremolo")) { ReplaceCategoryOrPlace(dto, "mod", "mod_tremolo", 4); count++; }

        if (ContainsAny(low, "크라이베이비", "cry baby", "crybaby", "와우", "wah")) { ReplaceCategoryOrPlace(dto, "eq", "eq_crybaby", 0); count++; }
        if (ContainsAny(low, "오렌지 스퀴저", "orange squeezer")) { ReplaceCategoryOrPlace(dto, "dyn", "comp_orangesqueezer", 0); count++; }
        if (ContainsAny(low, "타임라인", "timeline")) { ReplaceCategoryOrPlace(dto, "delay", "dly_timeline", 5); count++; }
        else if (ContainsAny(low, "dm2w", "dm-2w")) { ReplaceCategoryOrPlace(dto, "delay", "dly_dm2w", 5); count++; }
        else if (ContainsAny(low, "dd3t", "dd-3t")) { ReplaceCategoryOrPlace(dto, "delay", "dly_dd3t", 5); count++; }

        if (ContainsAny(low, "블루스카이", "bluesky", "쉬머", "shimmer"))
        {
            ReplaceCategoryOrPlace(dto, "reverb", "rev_bluesky", 6);
            if (ContainsAny(low, "쉬머", "shimmer"))
            {
                var revSlot = dto.Slots.FirstOrDefault(s => s.ModelId == "rev_bluesky");
                if (revSlot != null) { revSlot.Params[3] = Math.Max(revSlot.Params[3], 34f); revSlot.Params[4] = Math.Max(revSlot.Params[4], 40f); }
            }
            count++;
        }
        else if (ContainsAny(low, "rv6", "rv-6")) { ReplaceCategoryOrPlace(dto, "reverb", "rev_rv6", 6); count++; }

        return count;
    }

    private static void ApplyModifierTones(SignalChainConfigDto dto, string low)
    {
        if (ContainsAny(low, "게인 높", "강하게", "세게", "하이게인"))
        {
            foreach (var s in dto.Slots)
                if (!string.IsNullOrEmpty(s.ModelId) && Catalog.TryGetValue(s.ModelId, out var m) && m.Category is "amp" or "drive")
                    s.Params[0] = Math.Clamp(s.Params[0] + 1.4f, 0f, 10f);
        }
        if (ContainsAny(low, "병렬", "패럴렐", "parallel"))
            dto.RoutingMode = 1;
    }

    private static void ReplaceCategoryOrPlace(SignalChainConfigDto dto, string category, string modelId, int preferSlot)
    {
        for (int i = 0; i < dto.Slots.Count; i++)
        {
            var cur = dto.Slots[i];
            if (!string.IsNullOrEmpty(cur.ModelId) && Catalog.TryGetValue(cur.ModelId, out var meta) && meta.Category == category)
            {
                if (cur.ModelId == modelId) return;
                SetSlot(dto, i, modelId, true);
                return;
            }
        }
        if (string.IsNullOrEmpty(dto.Slots[preferSlot].ModelId))
        {
            SetSlot(dto, preferSlot, modelId, true);
            return;
        }
        for (int i = 0; i < dto.Slots.Count; i++)
        {
            if (string.IsNullOrEmpty(dto.Slots[i].ModelId))
            {
                SetSlot(dto, i, modelId, true);
                return;
            }
        }
        SetSlot(dto, preferSlot, modelId, true);
    }

    private static SignalChainConfigDto CreateBaseChain(string presetName, int routingMode, int splitCol = 1, int mergeCol = 5, float parallelMix = 0.42f)
    {
        return new SignalChainConfigDto
        {
            PresetId = $"AI{DateTime.Now:mmss}",
            PresetName = presetName,
            ActiveScene = 3,
            RoutingMode = routingMode,
            SplitCol = splitCol,
            MergeCol = mergeCol,
            ParallelMix = parallelMix,
            InputGainDb = 0f,
            OutputGainDb = 0f,
            Slots = CreateEmptySlotDtos(),
        };
    }

    private static List<ChainSlotDto> CreateEmptySlotDtos()
    {
        var list = new List<ChainSlotDto>(SignalChainConfigDto.SlotCount);
        for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
            list.Add(new ChainSlotDto { Index = i, ModelId = "", Enabled = true, Params = new float[8] });
        return list;
    }

    private static void SetSlot(SignalChainConfigDto dto, int index, string modelId, bool enabled, params float[] overrides)
    {
        if (index < 0 || index >= SignalChainConfigDto.SlotCount || !Catalog.TryGetValue(modelId, out var meta))
            return;
        var p = new float[8];
        Array.Copy(meta.Defaults, p, Math.Min(8, meta.Defaults.Length));
        for (int i = 0; i < Math.Min(8, overrides.Length); i++)
            p[i] = overrides[i];
        dto.Slots[index] = new ChainSlotDto { Index = index, ModelId = modelId, Enabled = enabled, Params = p };
    }

    public static SignalChainConfigDto ValidateAndNormalizeChain(SignalChainConfigDto raw, string fallbackName)
    {
        var norm = raw.Clone();
        if (string.IsNullOrWhiteSpace(norm.PresetName) || norm.PresetName == "Custom")
            norm.PresetName = fallbackName;
        if (string.IsNullOrWhiteSpace(norm.PresetId))
            norm.PresetId = $"AI{DateTime.Now:mmss}";
        norm.ActiveScene = Math.Clamp(norm.ActiveScene, 0, 7);
        norm.RoutingMode = Math.Clamp(norm.RoutingMode, 0, 1);
        norm.SplitCol = Math.Clamp(norm.SplitCol, 0, 7);
        norm.MergeCol = Math.Clamp(norm.MergeCol, norm.SplitCol, 7);
        norm.ParallelMix = Math.Clamp(norm.ParallelMix, 0f, 1f);
        norm.TempoBpm = Math.Clamp(norm.TempoBpm > 1f ? norm.TempoBpm : 120f, 40f, 280f);

        for (int i = 0; i < norm.Slots.Count; i++)
        {
            var sl = norm.Slots[i];
            sl.Index = i;
            sl.SyncDivision = Math.Clamp(sl.SyncDivision, 0, 5);
            string mid = (sl.ModelId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(mid) || !Catalog.TryGetValue(mid, out var meta))
            {
                sl.ModelId = "";
                sl.SyncDivision = 0;
                Array.Clear(sl.Params);
                continue;
            }
            sl.ModelId = meta.Id;
            for (int k = 0; k < Math.Min(sl.Params.Length, meta.Ranges.Length); k++)
            {
                var (mn, mx) = meta.Ranges[k];
                if (!float.IsFinite(sl.Params[k]))
                    sl.Params[k] = k < meta.Defaults.Length ? meta.Defaults[k] : mn;
                else
                    sl.Params[k] = Math.Clamp(sl.Params[k], mn, mx);
            }
        }

        // Build all 8 Scene snapshots (A..H) matching cortexCatalog.ts behavior
        norm.Scenes = new List<SceneSnapshotDto>(8);
        for (int scIdx = 0; scIdx < 8; scIdx++)
        {
            var sc = new SceneSnapshotDto { ParallelMix = norm.ParallelMix };
            for (int i = 0; i < norm.Slots.Count; i++)
            {
                var s = norm.Slots[i];
                bool en = scIdx == norm.ActiveScene ? s.Enabled : GetSceneDefaultEnabled(s.ModelId, s.Enabled, scIdx);
                var pCopy = new float[8];
                Array.Copy(s.Params, pCopy, 8);
                sc.Slots.Add(new SceneSlotStateDto { Enabled = en, SyncDivision = s.SyncDivision, Params = pCopy });
            }
            norm.Scenes.Add(sc);
        }

        return norm;
    }

    private static bool GetSceneDefaultEnabled(string modelId, bool baseEnabled, int sceneIdx)
    {
        if (string.IsNullOrEmpty(modelId) || !Catalog.TryGetValue(modelId, out var meta))
            return baseEnabled;

        string cat = meta.Category;
        return sceneIdx switch
        {
            0 => cat is "drive" or "delay" or "mod" ? false : cat == "reverb" ? true : baseEnabled,
            1 => cat == "drive" ? true : cat is "delay" or "mod" ? false : cat == "reverb" ? true : baseEnabled,
            2 => cat == "drive" ? true : cat is "delay" or "reverb" or "mod" ? false : baseEnabled,
            3 => baseEnabled,
            4 => cat is "drive" or "delay" or "reverb" ? true : cat == "mod" ? false : baseEnabled,
            5 => cat == "drive" ? false : cat is "mod" or "delay" or "reverb" ? true : baseEnabled,
            6 => cat is "drive" or "mod" or "delay" or "reverb" ? false : baseEnabled,
            7 => cat is "mod" or "delay" or "reverb" ? true : baseEnabled,
            _ => baseEnabled,
        };
    }

    private static string[] ExtractGearHighlights(SignalChainConfigDto chain)
    {
        var names = new List<string>();
        foreach (var s in chain.Slots)
        {
            if (s.Enabled && !string.IsNullOrEmpty(s.ModelId) && Catalog.TryGetValue(s.ModelId, out var meta))
                names.Add(meta.Name);
        }
        return names.ToArray();
    }

    private static string InferShortPresetTitle(string prompt)
    {
        string clean = prompt.Trim().Replace("\r", " ").Replace("\n", " ");
        if (clean.Length > 24) clean = clean[..24].Trim() + "…";
        return string.IsNullOrWhiteSpace(clean) ? "Antigravity AI Rig" : clean;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
            if (haystack.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static SignalChainConfigDto EnsureScenes(SignalChainConfigDto raw)
    {
        var dto = raw.Clone();
        if (dto.Scenes is { Count: 8 }) return dto;

        dto.Scenes = new List<SceneSnapshotDto>(8);
        for (int scIdx = 0; scIdx < 8; scIdx++)
        {
            var sc = new SceneSnapshotDto { ParallelMix = dto.ParallelMix };
            for (int i = 0; i < dto.Slots.Count; i++)
            {
                var s = dto.Slots[i];
                bool en = scIdx == dto.ActiveScene ? s.Enabled : GetSceneDefaultEnabled(s.ModelId, s.Enabled, scIdx);
                var pCopy = new float[8];
                Array.Copy(s.Params, pCopy, 8);
                sc.Slots.Add(new SceneSlotStateDto { Enabled = en, Params = pCopy });
            }
            dto.Scenes.Add(sc);
        }
        return dto;
    }

    public static SignalChainConfigDto ParsePresetFromJson(JsonElement root)
    {
        var dto = CreateBaseChain(
            root.TryGetProperty("presetName", out var pn) && pn.ValueKind == JsonValueKind.String ? (pn.GetString() ?? "Antigravity Preset") : "Antigravity Preset",
            root.TryGetProperty("routingMode", out var rm) && rm.TryGetInt32(out int rmi) ? rmi : 0,
            root.TryGetProperty("splitCol", out var sc) && sc.TryGetInt32(out int sci) ? sci : 1,
            root.TryGetProperty("mergeCol", out var mc) && mc.TryGetInt32(out int mci) ? mci : 5,
            root.TryGetProperty("parallelMix", out var pm) && pm.TryGetSingle(out float pmf) ? pmf : 0.42f);

        if (root.TryGetProperty("presetId", out var pidEl) && pidEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pidEl.GetString()))
            dto.PresetId = pidEl.GetString()!;
        if (root.TryGetProperty("activeScene", out var asEl) && asEl.TryGetInt32(out int asi))
            dto.ActiveScene = Math.Clamp(asi, 0, 7);
        if (root.TryGetProperty("tempoBpm", out var tbEl) && tbEl.TryGetSingle(out float tbf))
            dto.TempoBpm = Math.Clamp(tbf, 40f, 280f);
        if (root.TryGetProperty("inputGainDb", out var igEl) && igEl.TryGetSingle(out float igf))
            dto.InputGainDb = Math.Clamp(igf, -18f, 18f);
        if (root.TryGetProperty("outputGainDb", out var ogEl) && ogEl.TryGetSingle(out float ogf))
            dto.OutputGainDb = Math.Clamp(ogf, -18f, 18f);

        if (root.TryGetProperty("slots", out var slotsEl) && slotsEl.ValueKind == JsonValueKind.Array)
        {
            int seqIdx = 0;
            foreach (var item in slotsEl.EnumerateArray())
            {
                int targetIdx = item.TryGetProperty("index", out var ixEl) && ixEl.TryGetInt32(out int iv) ? iv : seqIdx;
                seqIdx++;
                if (targetIdx < 0 || targetIdx >= SignalChainConfigDto.SlotCount) continue;

                string mid = item.TryGetProperty("modelId", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? (mEl.GetString() ?? "").Trim().ToLowerInvariant()
                    : "";
                if (string.IsNullOrEmpty(mid) || !Catalog.TryGetValue(mid, out var meta))
                {
                    dto.Slots[targetIdx] = new ChainSlotDto { Index = targetIdx, ModelId = "", Enabled = true, Params = new float[8] };
                    continue;
                }

                bool enabled = !item.TryGetProperty("enabled", out var enEl) || enEl.ValueKind != JsonValueKind.False;
                int syncDiv = item.TryGetProperty("syncDivision", out var sdEl) && sdEl.TryGetInt32(out int sdi) ? Math.Clamp(sdi, 0, 5) : 0;
                var parms = new float[8];
                Array.Copy(meta.Defaults, parms, Math.Min(8, meta.Defaults.Length));

                if (item.TryGetProperty("params", out var pEl) && pEl.ValueKind == JsonValueKind.Array)
                {
                    int k = 0;
                    foreach (var v in pEl.EnumerateArray())
                    {
                        if (k >= 8) break;
                        if (v.TryGetSingle(out float fv) && float.IsFinite(fv))
                            parms[k] = fv;
                        k++;
                    }
                }
                dto.Slots[targetIdx] = new ChainSlotDto { Index = targetIdx, ModelId = meta.Id, Enabled = enabled, SyncDivision = syncDiv, Params = parms };
            }
        }

        return ValidateAndNormalizeChain(dto, dto.PresetName);
    }

    public static SignalChainConfigDto ApplySlotEdit(
        SignalChainConfigDto current,
        int slotIndex,
        string? modelId,
        bool? enabled,
        JsonElement paramsEl,
        int? syncDivision = null)
    {
        int idx = Math.Clamp(slotIndex, 0, SignalChainConfigDto.SlotCount - 1);
        var dto = EnsureScenes(current);
        var slot = dto.Slots[idx];
        bool modelChanged = false;

        if (modelId != null)
        {
            string normId = modelId.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normId))
            {
                slot.ModelId = "";
                slot.SyncDivision = 0;
                Array.Clear(slot.Params);
                modelChanged = true;
            }
            else if (Catalog.TryGetValue(normId, out var meta))
            {
                if (!string.Equals(slot.ModelId, meta.Id, StringComparison.OrdinalIgnoreCase))
                {
                    slot.ModelId = meta.Id;
                    slot.SyncDivision = 0;
                    Array.Clear(slot.Params);
                    Array.Copy(meta.Defaults, slot.Params, Math.Min(8, meta.Defaults.Length));
                    modelChanged = true;
                }
            }
            else
            {
                throw new InvalidOperationException($"알 수 없는 modelId: {modelId}");
            }
        }

        if (enabled.HasValue)
            slot.Enabled = enabled.Value;
        if (syncDivision.HasValue)
            slot.SyncDivision = Math.Clamp(syncDivision.Value, 0, 5);

        if (paramsEl.ValueKind == JsonValueKind.Array)
        {
            int k = 0;
            foreach (var v in paramsEl.EnumerateArray())
            {
                if (k >= 8) break;
                if (v.TryGetSingle(out float fv) && float.IsFinite(fv))
                    slot.Params[k] = fv;
                k++;
            }
        }
        else if (paramsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in paramsEl.EnumerateObject())
            {
                if (int.TryParse(prop.Name, out int pIdx) && pIdx >= 0 && pIdx < 8
                    && prop.Value.TryGetSingle(out float fv) && float.IsFinite(fv))
                {
                    slot.Params[pIdx] = fv;
                }
            }
        }

        if (!string.IsNullOrEmpty(slot.ModelId) && Catalog.TryGetValue(slot.ModelId, out var m))
        {
            for (int k = 0; k < Math.Min(slot.Params.Length, m.Ranges.Length); k++)
                slot.Params[k] = Math.Clamp(slot.Params[k], m.Ranges[k].Min, m.Ranges[k].Max);
        }

        if (dto.Scenes is { Count: 8 })
        {
            for (int scIdx = 0; scIdx < 8; scIdx++)
            {
                var sc = dto.Scenes[scIdx];
                if (sc.Slots.Count <= idx) continue;
                var scSlot = sc.Slots[idx];
                if (scIdx == dto.ActiveScene)
                {
                    scSlot.Enabled = slot.Enabled;
                    scSlot.SyncDivision = slot.SyncDivision;
                    Array.Copy(slot.Params, scSlot.Params, 8);
                }
                else if (modelChanged)
                {
                    scSlot.Enabled = string.IsNullOrEmpty(slot.ModelId) ? true : GetSceneDefaultEnabled(slot.ModelId, true, scIdx);
                    scSlot.SyncDivision = slot.SyncDivision;
                    Array.Copy(slot.Params, scSlot.Params, 8);
                }
            }
        }

        return dto;
    }

    public static SignalChainConfigDto SwitchScene(
        SignalChainConfigDto current,
        int? targetSceneIdx,
        int? routingMode = null,
        float? parallelMix = null,
        float? tempoBpm = null)
    {
        var dto = EnsureScenes(current);
        int curScene = Math.Clamp(dto.ActiveScene, 0, 7);

        // Save current active scene state first
        if (dto.Scenes is { Count: 8 })
        {
            var curSnap = dto.Scenes[curScene];
            curSnap.ParallelMix = dto.ParallelMix;
            for (int i = 0; i < Math.Min(dto.Slots.Count, curSnap.Slots.Count); i++)
            {
                curSnap.Slots[i].Enabled = dto.Slots[i].Enabled;
                curSnap.Slots[i].SyncDivision = dto.Slots[i].SyncDivision;
                Array.Copy(dto.Slots[i].Params, curSnap.Slots[i].Params, 8);
            }

            if (targetSceneIdx.HasValue)
            {
                int nextScene = Math.Clamp(targetSceneIdx.Value, 0, 7);
                var nextSnap = dto.Scenes[nextScene];
                dto.ActiveScene = nextScene;
                dto.ParallelMix = Math.Clamp(nextSnap.ParallelMix, 0f, 1f);
                for (int i = 0; i < Math.Min(dto.Slots.Count, nextSnap.Slots.Count); i++)
                {
                    if (!string.IsNullOrEmpty(dto.Slots[i].ModelId))
                    {
                        dto.Slots[i].Enabled = nextSnap.Slots[i].Enabled;
                        dto.Slots[i].SyncDivision = nextSnap.Slots[i].SyncDivision;
                        Array.Copy(nextSnap.Slots[i].Params, dto.Slots[i].Params, 8);
                    }
                }
            }
        }

        if (routingMode.HasValue)
            dto.RoutingMode = Math.Clamp(routingMode.Value, 0, 1);
        if (tempoBpm.HasValue)
            dto.TempoBpm = Math.Clamp(tempoBpm.Value, 40f, 280f);
        if (parallelMix.HasValue)
        {
            dto.ParallelMix = Math.Clamp(parallelMix.Value, 0f, 1f);
            if (dto.Scenes is { Count: 8 })
                dto.Scenes[Math.Clamp(dto.ActiveScene, 0, 7)].ParallelMix = dto.ParallelMix;
        }

        return dto;
    }
}
