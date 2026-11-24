using System.Text;
using System.Text.Json;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Serialization;

namespace NeuralRigStudio.Services;

/// <summary>
/// 하위 호환용 파라미터화 클리핑 토폴로지 열거형 (신규 SPICE 장비는 SpiceCircuitDto를 사용한다).
/// </summary>
public enum CustomClippingTopology : byte
{
    SiliconSymmetric = 0,
    SiliconAsymmetric = 1,
    Germanium = 2,
    RedLed = 3,
    Mosfet = 4,
    Jfet = 5,
    BjtFuzz = 6,
    Lm308Slew = 7,
    SchottkyBlend = 8,
}

/// <summary>
/// AI 에이전트가 조사·검증·등록한 커스텀 페달/앰프/캐비넷 스펙 DTO.
/// SPICE MNA 넷리스트(SpiceCircuit) 또는 실측 캐비넷 커브(CabAcoustic)를 포함한다.
/// </summary>
public sealed class CustomDeviceSpecDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string Category { get; set; } = "drive"; // "drive" | "amp" | "cab"
    public string CategoryLabel { get; set; } = "Custom SPICE Pedal";
    public string SchematicSourceUrl { get; set; } = "";
    public string SchematicSummary { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ── True SPICE MNA Netlist & Acoustic IR Definitions (Primary) ──
    public SpiceCircuitDto? SpiceCircuit { get; set; }
    public CustomCabAcousticDto? CabAcoustic { get; set; }
    public string[] ReferencedModels { get; set; } = Array.Empty<string>();

    // ── Legacy Parametric Fields (backward compatibility for existing JSON) ──
    public byte ClippingKind { get; set; }
    public float InputHpfHz { get; set; } = 95f;
    public float ToneMinHz { get; set; } = 1000f;
    public float ToneMaxHz { get; set; } = 6800f;
    public float MidPeakHz { get; set; } = 850f;
    public float MidPeakDb { get; set; } = 2.0f;
    public float MaxGainDb { get; set; } = 32f;
    public float Asymmetry { get; set; } = 0.08f;

    public byte BaseAmpChannel { get; set; } = 1;
    public float PreampCathodeHz { get; set; } = 160f;
    public float VoicingMidHz { get; set; } = 900f;
    public float VoicingMidDb { get; set; } = 1.5f;
    public float HighShelfDb { get; set; } = 1.0f;
    public float DefaultSag { get; set; } = 3.5f;

    public float SpeakerHpHz { get; set; } = 82f;
    public float SpeakerHpQ { get; set; } = 1.4f;
    public float BodyHz { get; set; } = 115f;
    public float BodyAmt { get; set; } = 0.50f;
    public float ConeMidHz { get; set; } = 2250f;
    public float ConeMidAmt { get; set; } = 0.65f;
    public float ConeLpHz { get; set; } = 4850f;
    public float ErDelayMs { get; set; } = 1.5f;

    public float[] Defaults { get; set; } = new float[] { 4.8f, 5.5f, 5.8f, 5.0f, 0f, 0f, 0f, 0f };
    public string[] ParamLabels { get; set; } = new[] { "DRIVE", "TONE", "LEVEL", "CHARACTER" };
}

/// <summary>
/// %AppData%/NeuralRigStudio/custom_devices.json 기반 동적 커스텀 디바이스 레지스트리 및 Cab IR 캐시.
/// </summary>
public static class CustomDeviceRegistry
{
    private static readonly object SyncLock = new();
    private static readonly Dictionary<string, CustomDeviceSpecDto> Devices = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (int sampleRate, PartitionedIr ir)> CabIrCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    private static string StorePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NeuralRigStudio");
            return Path.Combine(dir, "custom_devices.json");
        }
    }

    public static void LoadAll()
    {
        SpiceModelLibrary.LoadAll();
        lock (SyncLock)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(StorePath)) return;
                var list = JsonSerializer.Deserialize(
                    File.ReadAllText(StorePath),
                    NeuralRigJsonContext.Default.ListCustomDeviceSpecDto);
                if (list == null) return;
                foreach (var d in list)
                {
                    if (!string.IsNullOrWhiteSpace(d.Id))
                        Devices[d.Id.Trim().ToLowerInvariant()] = NormalizeSpec(d);
                }
            }
            catch
            {
                // 손상된 커스텀 목록은 무시하고 빈 상태로 시작
            }
        }
    }

    public static void ReloadFromDisk()
    {
        lock (SyncLock)
        {
            _loaded = false;
            Devices.Clear();
            CabIrCache.Clear();
        }
        LoadAll();
    }

    public static CustomDeviceSpecDto[] GetAll()
    {
        LoadAll();
        lock (SyncLock)
        {
            return Devices.Values.OrderBy(d => d.Category).ThenBy(d => d.Name).ToArray();
        }
    }

    public static bool TryGet(string? modelId, out CustomDeviceSpecDto? spec)
    {
        LoadAll();
        spec = null;
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        lock (SyncLock)
        {
            return TryGetLocked(modelId.Trim(), out spec);
        }
    }

    private static bool TryGetLocked(string q, out CustomDeviceSpecDto? spec)
    {
        if (Devices.TryGetValue(q, out spec))
            return true;

        string qCompact = AntigravityToneArchitect.CompactSearchKey(q);
        CustomDeviceSpecDto? exactFieldMatch = null;
        CustomDeviceSpecDto? compactMatch = null;

        foreach (var d in Devices.Values)
        {
            if (IsExactDeviceMatch(d, q))
            {
                exactFieldMatch = d;
                break;
            }
            if (compactMatch == null && qCompact.Length > 0 && IsCompactDeviceMatch(d, qCompact))
                compactMatch = d;
        }

        spec = exactFieldMatch ?? compactMatch;
        return spec != null;
    }

    private static bool IsExactDeviceMatch(CustomDeviceSpecDto d, string q)
    {
        if (string.Equals(d.OriginalName, q, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Name, q, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.ShortName, q, StringComparison.OrdinalIgnoreCase))
            return true;
        if (d.Aliases != null)
        {
            foreach (var a in d.Aliases)
                if (string.Equals(a, q, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
    }

    private static bool IsCompactDeviceMatch(CustomDeviceSpecDto d, string qCompact)
    {
        if (AntigravityToneArchitect.CompactSearchKey(d.OriginalName) == qCompact
            || AntigravityToneArchitect.CompactSearchKey(d.Name) == qCompact
            || AntigravityToneArchitect.CompactSearchKey(d.ShortName) == qCompact)
            return true;
        if (d.Aliases != null)
        {
            foreach (var a in d.Aliases)
                if (AntigravityToneArchitect.CompactSearchKey(a) == qCompact)
                    return true;
        }
        return false;
    }

    public static CustomDeviceSpecDto RegisterOrUpdate(CustomDeviceSpecDto rawSpec)
    {
        LoadAll();
        var spec = NormalizeSpec(rawSpec);
        lock (SyncLock)
        {
            Devices[spec.Id] = spec;
            CabIrCache.Remove(spec.Id);
            SaveLocked();
        }
        AntigravityToneArchitect.RegisterCustomDevice(spec);
        return spec;
    }

    public static bool Remove(string modelId)
    {
        LoadAll();
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        string targetId = modelId.Trim();
        bool removed;
        lock (SyncLock)
        {
            if (!Devices.ContainsKey(targetId) && TryGetLocked(targetId, out var matched) && matched != null)
                targetId = matched.Id;

            removed = Devices.Remove(targetId);
            CabIrCache.Remove(targetId);
            if (removed) SaveLocked();
        }
        if (removed) AntigravityToneArchitect.UnregisterCustomDevice(targetId);
        return removed;
    }

    public static PartitionedIr? GetOrCompileCabIr(CustomDeviceSpecDto spec, int sampleRate)
    {
        int sr = Math.Max(8000, sampleRate);
        lock (SyncLock)
        {
            if (CabIrCache.TryGetValue(spec.Id, out var cached) && cached.sampleRate == sr)
                return cached.ir;
        }

        PartitionedIr compiled;
        var cab = spec.CabAcoustic;
        if (cab != null && !string.IsNullOrWhiteSpace(cab.WavIrPath)
            && CabinetIrBank.TryLoadWavIrFromFile(cab.WavIrPath, sr, out var wavIr) && wavIr != null)
        {
            compiled = wavIr;
        }
        else if (cab != null && cab.MeasuredCurve is { Count: >= 3 })
        {
            var pts = new (double f, double db)[cab.MeasuredCurve.Count];
            for (int i = 0; i < cab.MeasuredCurve.Count; i++)
                pts[i] = (Math.Max(15.0, cab.MeasuredCurve[i].FreqHz), cab.MeasuredCurve[i].Db);
            compiled = CabinetIrBank.CompileCustomCabFromMeasuredCurve(
                sr, pts, Math.Clamp(cab.ErDelayMs, 0.8f, 4.0f), Math.Clamp(cab.ErGain, -0.35f, 0.35f));
        }
        else if (cab != null)
        {
            compiled = CabinetIrBank.CompileCustomCabIr(
                sr,
                Math.Clamp(cab.ResonanceHz, 50f, 130f),
                cab.EnclosureType.Contains("Open", StringComparison.OrdinalIgnoreCase) ? 1.65 : 1.35,
                Math.Clamp(cab.ResonanceHz * 1.45, 85f, 175f),
                0.55,
                2200.0,
                0.60,
                Math.Clamp(cab.HighRollOffHz, 3200f, 8200f),
                Math.Clamp(cab.ErDelayMs, 0.8f, 3.5f));
        }
        else
        {
            compiled = CabinetIrBank.CompileCustomCabIr(
                sr,
                Math.Clamp(spec.SpeakerHpHz, 50f, 125f),
                Math.Clamp(spec.SpeakerHpQ, 1.0f, 2.2f),
                Math.Clamp(spec.BodyHz, 85f, 175f),
                Math.Clamp(spec.BodyAmt, 0.1f, 0.9f),
                Math.Clamp(spec.ConeMidHz, 1200f, 4500f),
                Math.Clamp(spec.ConeMidAmt, 0.1f, 1.0f),
                Math.Clamp(spec.ConeLpHz, 3400f, 8200f),
                Math.Clamp(spec.ErDelayMs, 0.9f, 3.2f));
        }

        lock (SyncLock)
        {
            CabIrCache[spec.Id] = (sr, compiled);
        }
        return compiled;
    }

    private static void SaveLocked()
    {
        try
        {
            string? dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var list = Devices.Values.OrderBy(d => d.Id).ToList();
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list, NeuralRigIndentedJsonContext.Default.ListCustomDeviceSpecDto));
        }
        catch
        {
            // 디스크 저장 실패 시에도 메모리 레지스트리는 유지
        }
    }

    private static CustomDeviceSpecDto NormalizeSpec(CustomDeviceSpecDto s)
    {
        string cat = (s.Category ?? "drive").Trim().ToLowerInvariant() switch
        {
            "amp" => "amp",
            "cab" => "cab",
            _ => "drive",
        };
        string rawId = (s.Id ?? "").Trim().ToLowerInvariant();
        if (!rawId.StartsWith("custom_"))
            rawId = $"custom_{cat}_{SanitizeSlug(string.IsNullOrWhiteSpace(s.ShortName) ? s.Name : s.ShortName)}";

        var defs = new float[8];
        if (s.SpiceCircuit?.Knobs is { Count: > 0 } knobs)
        {
            foreach (var k in knobs)
            {
                if (k.Index is >= 0 and < 8)
                    defs[k.Index] = float.IsFinite(k.Default) ? Math.Clamp(k.Default, k.Min, k.Max) : 5f;
            }
        }
        else if (s.Defaults != null)
        {
            Array.Copy(s.Defaults, defs, Math.Min(8, s.Defaults.Length));
        }

        var labels = s.SpiceCircuit?.Knobs is { Count: > 0 } kList
            ? kList.OrderBy(k => k.Index).Select(k => k.Label).ToArray()
            : s.ParamLabels;

        var referencedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (s.ReferencedModels != null)
            foreach (var m in s.ReferencedModels) if (!string.IsNullOrWhiteSpace(m)) referencedModels.Add(m.Trim());
        if (s.SpiceCircuit?.Stages != null)
        {
            foreach (var st in s.SpiceCircuit.Stages)
            {
                if (st?.Elements == null) continue;
                foreach (var el in st.Elements)
                {
                    if (!string.IsNullOrWhiteSpace(el?.Model))
                        referencedModels.Add(el.Model.Trim());
                }
            }
        }

        string normName = string.IsNullOrWhiteSpace(s.Name) ? rawId : s.Name.Trim();
        string origName = string.IsNullOrWhiteSpace(s.OriginalName) ? normName : s.OriginalName.Trim();
        string[] normalizedAliases = BuildNormalizedAliases(origName, s.Aliases);

        return new CustomDeviceSpecDto
        {
            Id = rawId,
            Name = normName,
            ShortName = string.IsNullOrWhiteSpace(s.ShortName) ? Truncate(normName, 12) : Truncate(s.ShortName.Trim(), 12),
            OriginalName = origName,
            Aliases = normalizedAliases,
            Category = cat,
            CategoryLabel = s.SpiceCircuit != null
                ? cat switch { "amp" => "SPICE Tube/SS Amp", "cab" => "Custom IR Cab", _ => "SPICE Schematic Pedal" }
                : cat switch { "amp" => "Custom Schematic Amp", "cab" => "Custom Schematic Cab", _ => "Custom Schematic Pedal" },
            SchematicSourceUrl = s.SchematicSourceUrl ?? "",
            SchematicSummary = s.SchematicSummary ?? "",
            CreatedAtUtc = s.CreatedAtUtc == default ? DateTime.UtcNow : s.CreatedAtUtc,
            SpiceCircuit = s.SpiceCircuit,
            CabAcoustic = s.CabAcoustic,
            ReferencedModels = referencedModels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClippingKind = s.ClippingKind,
            InputHpfHz = Math.Clamp(s.InputHpfHz, 30f, 1100f),
            ToneMinHz = Math.Clamp(s.ToneMinHz, 450f, 2500f),
            ToneMaxHz = Math.Clamp(s.ToneMaxHz, 2800f, 12000f),
            MidPeakHz = Math.Clamp(s.MidPeakHz, 220f, 3200f),
            MidPeakDb = Math.Clamp(s.MidPeakDb, -12f, 12f),
            MaxGainDb = Math.Clamp(s.MaxGainDb, 12f, 56f),
            Asymmetry = Math.Clamp(s.Asymmetry, 0f, 0.48f),
            BaseAmpChannel = (byte)Math.Clamp((int)s.BaseAmpChannel, 0, 2),
            PreampCathodeHz = Math.Clamp(s.PreampCathodeHz, 45f, 1800f),
            VoicingMidHz = Math.Clamp(s.VoicingMidHz, 300f, 2800f),
            VoicingMidDb = Math.Clamp(s.VoicingMidDb, -8f, 8f),
            HighShelfDb = Math.Clamp(s.HighShelfDb, -6f, 6f),
            DefaultSag = Math.Clamp(s.DefaultSag, 1.0f, 8.0f),
            SpeakerHpHz = Math.Clamp(s.SpeakerHpHz, 50f, 125f),
            SpeakerHpQ = Math.Clamp(s.SpeakerHpQ, 1.0f, 2.2f),
            BodyHz = Math.Clamp(s.BodyHz, 85f, 175f),
            BodyAmt = Math.Clamp(s.BodyAmt, 0.1f, 0.9f),
            ConeMidHz = Math.Clamp(s.ConeMidHz, 1200f, 4500f),
            ConeMidAmt = Math.Clamp(s.ConeMidAmt, 0.1f, 1.0f),
            ConeLpHz = Math.Clamp(s.ConeLpHz, 3400f, 8200f),
            ErDelayMs = Math.Clamp(s.ErDelayMs, 0.9f, 3.2f),
            Defaults = defs,
            ParamLabels = labels is { Length: > 0 } ? labels : cat switch
            {
                "amp" => new[] { "GAIN", "BASS", "MID", "TREBLE", "MASTER", "SAG", "PRESENCE", "BRIGHT" },
                "cab" => new[] { "LOW CUT", "HIGH CUT", "LEVEL" },
                _ => new[] { "DRIVE", "TONE", "LEVEL", "CHARACTER" },
            },
        };
    }

    private static string[] BuildNormalizedAliases(string origName, string[]? rawAliases)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        void AddAlias(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            string trimmed = candidate.Trim();
            if (seen.Add(trimmed))
                result.Add(trimmed);

            // Also index hyphenless variants for hyphenated tokens like OD-1 -> OD1
            foreach (var tok in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!tok.Contains('-')) continue;
                string noHyphen = tok.Replace("-", "");
                if (noHyphen.Length >= 2 && seen.Add(noHyphen))
                    result.Add(noHyphen);
            }
        }

        AddAlias(origName);
        if (rawAliases != null)
        {
            foreach (var a in rawAliases)
                AddAlias(a);
        }
        return result.ToArray();
    }

    internal static string SanitizeSlug(string text)
    {
        var sb = new StringBuilder();
        bool lastUnderscore = false;
        foreach (char ch in (text ?? "").ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastUnderscore = false;
            }
            else if (!lastUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
            if (sb.Length >= 28) break;
        }
        return sb.ToString().Trim('_');
    }

    private static string Truncate(string? s, int max)
    {
        string t = (s ?? "").Trim();
        return t.Length <= max ? t : t[..max].Trim();
    }
}

/// <summary>
/// AI 에이전트 주도의 회로도 리서치 → 누락 부품 데이터시트 검색/등록 → MNA SPICE 시뮬레이션(.OP/.AC/.TRAN) → 커밋
/// 워크플로우 가이드 및 검증 레퍼런스 생성기.
/// </summary>
public static class SpiceAuthoringGuide
{
    public static object BuildGuidePayload()
    {
        return new
        {
            philosophy = "AI-Agent-Led Real-Time MNA SPICE Pipeline. NeuralRig MCP NEVER scrapes or guesses circuit values on its own; the AI agent researches real schematics & datasheets on the web, registers any missing semiconductor/tube .MODEL specs, verifies physics via .OP/.AC/.TRAN SPICE simulation, and commits the verified netlist.",
            mandatoryAntiHallucinationRules = new[]
            {
                "1. 사용자가 요청한 페달·앰프·캐비넷의 실제 회로도(Schematic), 부품값(BOM), 또는 공식 주파수 응답 커브를 웹 검색으로 먼저 조사하라.",
                "2. 인터넷에서 해당 장비의 회로도나 공식 음향 스펙을 찾을 수 없거나, 존재하지 않는 가상의 장비라면 절대로 부품값/트랜지스터/회로를 추측·할루시네이션하지 말고 사용자에게 '해당 장비의 실제 회로도/스펙을 찾을 수 없어 추가할 수 없습니다'라고 명확히 답변하고 중단하라.",
                "3. 회로도의 반도체/IC/진공관 부품(예: MA856, CA3080, 2SK30A, OPA2134, 6L6GC 등)이 neuralrig_list_spice_models에 없으면, 임의로 기존 부품으로 대체하지 말고 반드시 해당 부품의 실제 데이터시트/SPICE .MODEL 파라미터를 웹에서 검색해 neuralrig_register_spice_model로 등록하라 (datasheeetUrl 필수).",
                "4. 회로도 넷리스트 작성 후 반드시 neuralrig_simulate_spice를 호출하여 .OP(직류 바이어스), .AC(주파수 응답), .TRAN(4x 비선형 THD/대칭성), RT Alloc(0 B)이 물리적으로 정상인지 확인한 뒤 neuralrig_commit_spice_device로 커밋하라.",
            },
            workflowSteps = new[]
            {
                "Step 1 [웹 리서치]: 웹 검색으로 대상 장비의 실제 회로도(ElectroSmash, GeneralGuitarGadgets, PedalPCB, Rob Robinette 등) 및 정확한 R/C/Pot 값과 능동소자 형번을 확보한다.",
                "Step 2 [부품 확인]: neuralrig_list_spice_models(checkParts=[...])로 회로에 쓰인 모든 반도체/진공관/OP-AMP 모델이 라이브러리에 있는지 확인한다.",
                "Step 3 [누락 부품 등록]: missingParts에 나온 부품은 데이터시트/SPICE .MODEL을 검색한 뒤 neuralrig_register_spice_model(name, kind, polarity, datasheetUrl, params)로 등록한다.",
                "Step 4 [넷리스트 작성 & 검증]: SpiceCircuitDto(또는 캐비넷용 CabAcousticDto)를 작성해 neuralrig_simulate_spice로 .OP/.AC/.TRAN 및 0 B RT 검증을 통과시킨다.",
                "Step 5 [커밋 & 배치]: neuralrig_commit_spice_device로 NeuralRig Studio에 영구 등록한다. 상표권 회피용 가칭(`name`, `shortName`)을 표시 이름으로 넣고, 검색 및 MCP 조회용 실제 모델명(`originalName`)과 별칭(`aliases`)을 함께 지정하며 필요시 slotIndex(0..15)에 배치한다.",
            },
            netlistSyntax = new
            {
                elements = new
                {
                    R = "Resistor: nodes=[n0, n1], valueText='4.7k' (supports p,n,u,m,k,M,Meg,G and 4k7 notation)",
                    C = "Capacitor (Generalized Trapezoidal theta=0.55): nodes=[n0, n1], valueText='47n'",
                    L = "Inductor: nodes=[n0, n1], valueText='500mH'",
                    POT = "3-pin Potentiometer: nodes=[CW_pin3, Wiper_pin2, CCW_pin1] (use 'nc' for unconnected end pin), valueText='500k', knobIndex=0..7, taper='LIN'|'LOG'|'RLOG', reversed=false",
                    SW = "Knob/Toggle Switch: nodes=[n0, n1], knobIndex=0..7, threshold=0.5, closedOhms=0.05, openOhms=1e8",
                    D = "Shockley Diode (+ parasitic Rs): nodes=[anode, cathode], model='1N914'",
                    Q = "BJT (Ebers-Moll + Early Vaf): nodes=[C, B, E], model='2N5088'|'AC128'",
                    J = "JFET (Shichman-Hodges + gate diodes): nodes=[D, G, S], model='J201'",
                    M = "MOSFET (Level 1 + integral body diode): nodes=[D, G, S], model='2N7000'",
                    U = "Op-Amp (Aol, GBW pole, SlewRate V/us, tanh rails, Rin, Rout): nodes=[in+, in-, out], model='LM308'|'JRC4558'",
                    T = "Triode (Norman Koren + grid current): nodes=[Plate, Grid, Cathode], model='12AX7'",
                },
                groundNodes = new[] { "0", "gnd", "ground" },
                supplies = "Per stage supplies array e.g. [{node:'vcc', voltage:9.0}, {node:'vb', voltage:4.5}] or B+ [{node:'vp', voltage:250.0}]",
                stageBoundary = "Multi-stage circuits connect stage[i].outputNode -> stage[i+1].inputNode automatically (AC-centered by exact .OP DC bias subtraction).",
            },
            requiredModelParams = new
            {
                diode = "Is (>0), N (0.2..10), optional Rs (>=0)",
                bjt = "Is (>0), Bf (>=1), Nf (>0.5), optional Br, Nr, Vaf, polarity='N'|'P'",
                jfet = "Beta (>0), Vto, optional Lambda, Is, N, polarity='N'|'P'",
                mosfet = "Kp (>0), Vto, optional Lambda, BodyDiodeIs, BodyDiodeN, polarity='N'|'P'",
                opamp = "Aol (>10), GbwHz (>100), SlewRateVus (>0.01), VrailPos, VrailNeg, Rin (>0), Rout (>0)",
                triode = "Mu (>1), Ex (>0.5), Kg1 (>1), Kp (>1), Kvb (>0), optional RgGrid, VgkClip",
            },
            referenceExamplePedalNetlist = new
            {
                note = "Verified Ibanez TS808 Clipping + Tone Stage in MNA SPICE format (4.5V virtual bias, 4.7k/47nF 720Hz HPF, JRC4558 + anti-parallel 1N914)",
                id = "custom_drive_ts808_spice",
                name = "Green Screamer 808 [SPICE]",
                shortName = "Green 808",
                originalName = "Ibanez TS808 Tube Screamer",
                aliases = new[] { "Ibanez", "TS808", "TS-808", "Tube Screamer", "튜브스크리머" },
                category = "drive",
                schematicSourceUrl = "https://www.electrosmash.com/tube-screamer-analysis",
                spiceCircuit = new SpiceCircuitDto
                {
                    InputVoltageScale = 0.45f,
                    OutputDcBlock = true,
                    Knobs = new List<SpiceKnobDefDto>
                    {
                        new() { Index = 0, Label = "DRIVE", Min = 0f, Max = 10f, Default = 5.0f },
                        new() { Index = 1, Label = "TONE", Min = 0f, Max = 10f, Default = 5.5f },
                        new() { Index = 2, Label = "LEVEL", Min = 0f, Max = 10f, Default = 6.0f },
                    },
                    Stages = new List<SpiceStageDto>
                    {
                        new()
                        {
                            Name = "clipping_amp",
                            InputNode = "in",
                            OutputNode = "out",
                            InputSeriesResistance = 1000f,
                            Supplies = new List<SpiceFixedVoltageDto>
                            {
                                new() { Node = "vref", Voltage = 4.5 },
                            },
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
                            Name = "tone_level",
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
                },
            },
        };
    }
}
