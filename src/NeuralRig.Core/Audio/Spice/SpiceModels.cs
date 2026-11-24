using System.Globalization;
using System.Text.Json;
using NeuralRigStudio.Serialization;

namespace NeuralRigStudio.Audio.Spice;

/// <summary>
/// 검증된 SPICE 부품 데이터시트 모델 DTO (.MODEL 정의).
/// 할루시네이션을 막기 위해 AI 등록 모델은 반드시 datasheetUrl과 필수 물리 파라미터를 모두 가져야 한다.
/// </summary>
public sealed class SpiceComponentModelDto
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "diode"; // diode | bjt | jfet | mosfet | opamp | triode
    public string Polarity { get; set; } = "N"; // N (NPN/NJF/NMOS) | P (PNP/PJF/PMOS)
    public string DatasheetUrl { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public Dictionary<string, double> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetParam(string key, out double val)
    {
        if (Params != null)
        {
            if (Params.TryGetValue(key, out val))
                return double.IsFinite(val);
            foreach (var kv in Params)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    val = kv.Value;
                    return double.IsFinite(val);
                }
            }
        }
        val = 0.0;
        return false;
    }

    public double GetParamOrDefault(string key, double fallback)
        => TryGetParam(key, out double v) ? v : fallback;

    public SpiceComponentModelDto Clone()
    {
        var copy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (Params != null)
        {
            foreach (var kv in Params)
                copy[kv.Key] = kv.Value;
        }
        return new SpiceComponentModelDto
        {
            Name = Name,
            Kind = Kind,
            Polarity = Polarity,
            DatasheetUrl = DatasheetUrl,
            Notes = Notes,
            IsBuiltIn = IsBuiltIn,
            Params = copy,
        };
    }
}

/// <summary>
/// %AppData%/NeuralRigStudio/spice_models.json 기반 검증 부품 라이브러리.
/// 기본 29종 표준 기타 회로 반도체/진공관 모델 + AI 에이전트가 데이터시트에서 직접 조사·등록한 부품 모델을 관리한다.
/// </summary>
public static class SpiceModelLibrary
{
    private static readonly object SyncLock = new();
    private static readonly Dictionary<string, SpiceComponentModelDto> Models = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    private static string StorePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NeuralRigStudio");
            return Path.Combine(dir, "spice_models.json");
        }
    }

    public static void LoadAll()
    {
        lock (SyncLock)
        {
            if (_loaded) return;
            _loaded = true;
            SeedBuiltInsLocked();
            try
            {
                if (!File.Exists(StorePath)) return;
                var list = JsonSerializer.Deserialize(
                    File.ReadAllText(StorePath),
                    NeuralRigJsonContext.Default.ListSpiceComponentModelDto);
                if (list == null) return;
                foreach (var m in list)
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.Name)) continue;
                    var norm = Normalize(m);
                    var (ok, _) = ValidateModel(norm, requireDatasheetUrl: !norm.IsBuiltIn);
                    if (ok)
                        Models[norm.Name] = norm;
                }
            }
            catch
            {
                // 손상된 커스텀 모델 파일은 무시하고 기본 라이브러리 유지
            }
        }
    }

    public static void ReloadFromDisk()
    {
        lock (SyncLock)
        {
            _loaded = false;
            Models.Clear();
        }
        LoadAll();
    }

    public static SpiceComponentModelDto[] GetAll(string? kindFilter = null)
    {
        LoadAll();
        string k = (kindFilter ?? "").Trim().ToLowerInvariant();
        lock (SyncLock)
        {
            return Models.Values
                .Where(m => string.IsNullOrEmpty(k) || k == "all" || string.Equals(m.Kind, k, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Kind)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => m.Clone())
                .ToArray();
        }
    }

    public static bool TryGet(
        string? name,
        out SpiceComponentModelDto? model,
        Dictionary<string, SpiceComponentModelDto>? inlineOverrides = null)
    {
        LoadAll();
        model = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        string key = name.Trim();

        if (inlineOverrides != null)
        {
            if (inlineOverrides.TryGetValue(key, out var ov) && ov != null)
            {
                var normOv = Normalize(ov);
                if (string.IsNullOrWhiteSpace(normOv.Name)) normOv.Name = key;
                if (ValidateModel(normOv, requireDatasheetUrl: false).Ok)
                {
                    model = normOv;
                    return true;
                }
            }
            foreach (var kv in inlineOverrides)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                {
                    var normOv = Normalize(kv.Value);
                    if (string.IsNullOrWhiteSpace(normOv.Name)) normOv.Name = kv.Key;
                    if (ValidateModel(normOv, requireDatasheetUrl: false).Ok)
                    {
                        model = normOv;
                        return true;
                    }
                }
            }
        }

        lock (SyncLock)
        {
            if (Models.TryGetValue(key, out var found))
            {
                model = found.Clone();
                return true;
            }
        }
        return false;
    }

    public static (string[] Found, string[] Missing) CheckParts(IEnumerable<string> partNames)
    {
        LoadAll();
        var found = new List<string>();
        var missing = new List<string>();
        foreach (var raw in partNames)
        {
            string p = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(p)) continue;
            if (TryGet(p, out _))
            {
                if (!found.Contains(p, StringComparer.OrdinalIgnoreCase))
                    found.Add(p);
            }
            else
            {
                if (!missing.Contains(p, StringComparer.OrdinalIgnoreCase))
                    missing.Add(p);
            }
        }
        return (found.ToArray(), missing.ToArray());
    }

    public static (bool Ok, string Error) ValidateModel(SpiceComponentModelDto? model, bool requireDatasheetUrl)
    {
        if (model == null)
            return (false, "모델 객체가 null입니다.");
        string name = (model.Name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return (false, "부품 모델 name이 비어 있습니다.");

        string kind = (model.Kind ?? "").Trim().ToLowerInvariant();
        if (kind is not ("diode" or "bjt" or "jfet" or "mosfet" or "opamp" or "triode"))
            return (false, $"지원하지 않는 부품 kind '{model.Kind}'입니다. (diode|bjt|jfet|mosfet|opamp|triode)");

        if (requireDatasheetUrl)
        {
            string url = (model.DatasheetUrl ?? "").Trim();
            if (string.IsNullOrEmpty(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return (false, $"부품 '{name}' 등록에는 유효한 출처 datasheetUrl(http/https)이 필수입니다.");
            }
        }

        string pol = (model.Polarity ?? "N").Trim().ToUpperInvariant();
        if (kind is "bjt" or "jfet" or "mosfet" && pol is not ("N" or "P"))
            return (false, $"부품 '{name}'({kind})의 polarity는 'N' 또는 'P'여야 합니다.");

        static bool HasPos(SpiceComponentModelDto m, string k, out double v, double max = double.MaxValue)
            => m.TryGetParam(k, out v) && double.IsFinite(v) && v > 0.0 && v <= max;

        static bool HasFin(SpiceComponentModelDto m, string k, out double v)
            => m.TryGetParam(k, out v) && double.IsFinite(v);

        switch (kind)
        {
            case "diode":
                if (!HasPos(model, "Is", out _, 1e-1))
                    return (false, $"Diode '{name}'에는 양수의 'Is'(포화전류 A)가 필수입니다.");
                if (!HasPos(model, "N", out double nVal, 10.0) || nVal < 0.2)
                    return (false, $"Diode '{name}'에는 유효한 'N'(방출계수 0.2..10)이 필수입니다.");
                if (model.TryGetParam("Rs", out double rsVal) && rsVal < 0.0)
                    return (false, $"Diode '{name}'의 Rs는 0 이상이어야 합니다.");
                break;

            case "bjt":
                if (!HasPos(model, "Is", out _, 1e-1))
                    return (false, $"BJT '{name}'에는 양수의 'Is'가 필수입니다.");
                if (!HasPos(model, "Bf", out double bfVal, 10000.0) || bfVal < 1.0)
                    return (false, $"BJT '{name}'에는 유효한 'Bf'(순방향 전류이득 >= 1)가 필수입니다.");
                if (!HasPos(model, "Nf", out double nfVal, 5.0) || nfVal < 0.5)
                    return (false, $"BJT '{name}'에는 유효한 'Nf'(순방향 방출계수)가 필수입니다.");
                break;

            case "jfet":
                if (!HasPos(model, "Beta", out _, 10.0))
                    return (false, $"JFET '{name}'에는 양수의 'Beta'(트랜스컨덕턴스 계수 A/V^2)가 필수입니다.");
                if (!HasFin(model, "Vto", out double jVto) || Math.Abs(jVto) > 25.0)
                    return (false, $"JFET '{name}'에는 유효한 'Vto'(핀치오프 전압 V)가 필수입니다.");
                break;

            case "mosfet":
                if (!HasPos(model, "Kp", out _, 100.0))
                    return (false, $"MOSFET '{name}'에는 양수의 'Kp'(트랜스컨덕턴스 계수 A/V^2)가 필수입니다.");
                if (!HasFin(model, "Vto", out double mVto) || Math.Abs(mVto) > 25.0)
                    return (false, $"MOSFET '{name}'에는 유효한 'Vto'(문턱 전압 V)가 필수입니다.");
                break;

            case "opamp":
                if (!HasPos(model, "Aol", out double aol) || aol < 10.0)
                    return (false, $"OpAmp '{name}'에는 유효한 'Aol'(오픈루프 이득 >= 10)이 필수입니다.");
                if (!HasPos(model, "GbwHz", out double gbw) || gbw < 100.0)
                    return (false, $"OpAmp '{name}'에는 유효한 'GbwHz'(이득대역폭곱 Hz >= 100)이 필수입니다.");
                if (!HasPos(model, "SlewRateVus", out _))
                    return (false, $"OpAmp '{name}'에는 양수의 'SlewRateVus'(V/us)가 필수입니다.");
                if (!HasFin(model, "VrailPos", out _) || !HasFin(model, "VrailNeg", out _))
                    return (false, $"OpAmp '{name}'에는 'VrailPos'와 'VrailNeg'가 필수입니다.");
                if (!HasPos(model, "Rin", out _) || !HasPos(model, "Rout", out _))
                    return (false, $"OpAmp '{name}'에는 양수의 'Rin'과 'Rout'가 필수입니다.");
                break;

            case "triode":
                if (!HasPos(model, "Mu", out double mu) || mu < 1.0)
                    return (false, $"Triode '{name}'에는 'Mu'(증폭률 >= 1)가 필수입니다.");
                if (!HasPos(model, "Ex", out _) || !HasPos(model, "Kg1", out _) || !HasPos(model, "Kp", out _) || !HasPos(model, "Kvb", out _))
                    return (false, $"Triode '{name}'에는 Norman Koren 파라미터 'Ex', 'Kg1', 'Kp', 'Kvb'가 모두 필수입니다.");
                break;
        }

        return (true, "");
    }

    public static SpiceComponentModelDto RegisterOrUpdate(SpiceComponentModelDto raw)
    {
        LoadAll();
        var norm = Normalize(raw);
        norm.IsBuiltIn = false;
        var (ok, err) = ValidateModel(norm, requireDatasheetUrl: true);
        if (!ok)
            throw new InvalidOperationException(err);

        lock (SyncLock)
        {
            Models[norm.Name] = norm.Clone();
            SaveCustomLocked();
        }
        return norm;
    }

    public static bool RemoveCustom(string name)
    {
        LoadAll();
        if (string.IsNullOrWhiteSpace(name)) return false;
        lock (SyncLock)
        {
            if (Models.TryGetValue(name.Trim(), out var existing) && existing.IsBuiltIn)
                return false;
            bool removed = Models.Remove(name.Trim());
            if (removed) SaveCustomLocked();
            return removed;
        }
    }

    private static SpiceComponentModelDto Normalize(SpiceComponentModelDto m)
    {
        var p = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (m.Params != null)
        {
            foreach (var kv in m.Params)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key) && double.IsFinite(kv.Value))
                    p[kv.Key.Trim()] = kv.Value;
            }
        }
        string pol = (m.Polarity ?? "N").Trim().ToUpperInvariant();
        if (pol.StartsWith("P")) pol = "P";
        else if (pol.StartsWith("N")) pol = "N";
        else pol = "N";

        return new SpiceComponentModelDto
        {
            Name = (m.Name ?? "").Trim(),
            Kind = (m.Kind ?? "").Trim().ToLowerInvariant(),
            Polarity = pol,
            DatasheetUrl = (m.DatasheetUrl ?? "").Trim(),
            Notes = (m.Notes ?? "").Trim(),
            IsBuiltIn = m.IsBuiltIn,
            Params = p,
        };
    }

    private static void SaveCustomLocked()
    {
        try
        {
            string? dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var list = Models.Values
                .Where(m => !m.IsBuiltIn)
                .OrderBy(m => m.Kind)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list, NeuralRigIndentedJsonContext.Default.ListSpiceComponentModelDto));
        }
        catch
        {
            // 저장 실패 시에도 런타임 딕셔너리는 유지
        }
    }

    private static void SeedBuiltInsLocked()
    {
        void Add(string name, string kind, string polarity, string url, string notes, params (string k, double v)[] p)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in p) dict[k] = v;
            Models[name] = new SpiceComponentModelDto
            {
                Name = name,
                Kind = kind,
                Polarity = polarity,
                DatasheetUrl = url,
                Notes = notes,
                IsBuiltIn = true,
                Params = dict,
            };
        }

        // ── Diodes ──
        Add("1N914", "diode", "N", "https://www.vishay.com/docs/85622/1n914.pdf",
            "Fast silicon switching diode (TS808, RAT, Bluesbreaker)", ("Is", 2.52e-9), ("N", 1.75), ("Rs", 0.57));
        Add("1N4148", "diode", "N", "https://www.vishay.com/docs/81857/1n4148.pdf",
            "High-speed silicon switching diode (SD-1, Timmy, DS-1)", ("Is", 2.52e-9), ("N", 1.75), ("Rs", 0.57));
        Add("1N34A", "diode", "N", "https://www.nteinc.com/specs/100to199/pdf/nte109.pdf",
            "Germanium point-contact diode (~0.3V forward knee, Klon/OCD/Distortion+)", ("Is", 2.0e-6), ("N", 1.60), ("Rs", 45.0));
        Add("1N270", "diode", "N", "https://www.nteinc.com/specs/100to199/pdf/nte109.pdf",
            "Germanium diode (MXR Distortion+, DOD 250 Ge variant)", ("Is", 1.6e-6), ("N", 1.55), ("Rs", 38.0));
        Add("BAT41", "diode", "N", "https://www.st.com/resource/en/datasheet/bat41.pdf",
            "Small-signal Schottky diode (~0.35V knee, Zendrive)", ("Is", 1.0e-7), ("N", 1.05), ("Rs", 12.0));
        Add("RED_LED", "diode", "N", "https://www.vishay.com/docs/83071/tlhr540.pdf",
            "Standard 5mm Red LED (~1.65V Vf, Marshall Guv'nor, Turbo RAT)", ("Is", 1.0e-12), ("N", 3.20), ("Rs", 10.0));
        Add("GREEN_LED", "diode", "N", "https://www.vishay.com/docs/83071/tlhr540.pdf",
            "Standard 5mm Green LED (~1.9V Vf)", ("Is", 1.0e-14), ("N", 3.60), ("Rs", 12.0));
        Add("1N4001", "diode", "N", "https://www.vishay.com/docs/88503/1n4001.pdf",
            "1A Silicon rectifier diode", ("Is", 1.4e-8), ("N", 1.85), ("Rs", 0.04));

        // ── BJTs ──
        Add("2N5088", "bjt", "N", "https://www.onsemi.com/pdf/datasheet/2n5088-d.pdf",
            "High-gain low-noise NPN silicon BJT (Big Muff Pi)", ("Is", 5.91e-15), ("Bf", 650.0), ("Br", 4.0), ("Nf", 1.0), ("Nr", 1.0), ("Vaf", 62.0));
        Add("2N3904", "bjt", "N", "https://www.onsemi.com/pdf/datasheet/2n3903-d.pdf",
            "General purpose NPN silicon BJT", ("Is", 6.73e-15), ("Bf", 300.0), ("Br", 4.0), ("Nf", 1.0), ("Nr", 1.0), ("Vaf", 74.0));
        Add("BC108", "bjt", "N", "https://www.centralsemi.com/get_document.php?cmp=1&mergetype=pd&mergepath=pd&pdf_id=BC107-9.PDF",
            "Vintage metal-can NPN silicon transistor (Silicon Fuzz Face)", ("Is", 1.8e-14), ("Bf", 280.0), ("Br", 4.0), ("Nf", 1.0), ("Nr", 1.0), ("Vaf", 65.0));
        Add("BC109C", "bjt", "N", "https://www.centralsemi.com/get_document.php?cmp=1&mergetype=pd&mergepath=pd&pdf_id=BC107-9.PDF",
            "High-hFE low-noise NPN silicon transistor (Big Muff/Si Fuzz)", ("Is", 1.4e-14), ("Bf", 480.0), ("Br", 4.5), ("Nf", 1.0), ("Nr", 1.0), ("Vaf", 68.0));
        Add("AC128", "bjt", "P", "https://www.radiomuseum.org/tubes/tube_ac128.html",
            "Classic Mullard/Philips PNP Germanium BJT (Ge Fuzz Face, Tone Bender)", ("Is", 2.5e-6), ("Bf", 95.0), ("Br", 6.0), ("Nf", 1.25), ("Nr", 1.30), ("Vaf", 35.0));
        Add("OC44", "bjt", "P", "https://www.radiomuseum.org/tubes/tube_oc44.html",
            "Mullard OC44 PNP Germanium BJT (Dallas Rangemaster)", ("Is", 3.0e-6), ("Bf", 85.0), ("Br", 5.0), ("Nf", 1.25), ("Nr", 1.30), ("Vaf", 30.0));
        Add("2N3906", "bjt", "P", "https://www.onsemi.com/pdf/datasheet/2n3906-d.pdf",
            "General purpose PNP silicon BJT", ("Is", 1.41e-15), ("Bf", 220.0), ("Br", 3.5), ("Nf", 1.0), ("Nr", 1.0), ("Vaf", 60.0));

        // ── JFETs ──
        Add("J201", "jfet", "N", "https://www.interfet.com/jfet-datasheets/jfet-j201-j202-interfet.pdf",
            "Low-noise N-channel JFET (Fender/Marshall/Dumble amp-in-a-box)", ("Vto", -0.85), ("Beta", 1.5e-3), ("Lambda", 0.004), ("Is", 1.0e-13), ("N", 1.0));
        Add("2N5457", "jfet", "N", "https://www.onsemi.com/pdf/datasheet/2n5457-d.pdf",
            "General purpose N-channel JFET (Phase 90 / FET preamp)", ("Vto", -1.80), ("Beta", 1.1e-3), ("Lambda", 0.005), ("Is", 1.0e-13), ("N", 1.0));
        Add("MPF102", "jfet", "N", "https://www.onsemi.com/pdf/datasheet/mpf102-d.pdf",
            "High-Vp N-channel JFET", ("Vto", -3.00), ("Beta", 1.0e-3), ("Lambda", 0.005), ("Is", 1.0e-13), ("N", 1.0));

        // ── MOSFETs ──
        Add("2N7000", "mosfet", "N", "https://www.onsemi.com/pdf/datasheet/2n7000-d.pdf",
            "N-channel Enhancement MOSFET with integral body diode (OCD, Zendrive)", ("Vto", 2.1), ("Kp", 0.18), ("Lambda", 0.015), ("BodyDiodeIs", 2.5e-9), ("BodyDiodeN", 1.75));
        Add("BS170", "mosfet", "N", "https://www.onsemi.com/pdf/datasheet/bs170-d.pdf",
            "N-channel Enhancement MOSFET (ZVEX Super Hard On, Box of Rock)", ("Vto", 1.9), ("Kp", 0.22), ("Lambda", 0.015), ("BodyDiodeIs", 2.5e-9), ("BodyDiodeN", 1.75));

        // ── Op-Amps (9V single-supply virtual-ground swing defaults ~[1.4V, 7.6V], scaleable per circuit rails) ──
        Add("LM308", "opamp", "N", "https://www.ti.com/lit/ds/symlink/lm108a-n.pdf",
            "Classic low-slew-rate precision op-amp (ProCo RAT, 0.3V/us slew)", ("Aol", 100000.0), ("GbwHz", 300000.0), ("SlewRateVus", 0.30), ("VrailPos", 7.6), ("VrailNeg", 1.4), ("Rin", 4.0e7), ("Rout", 100.0));
        Add("LM741", "opamp", "N", "https://www.ti.com/lit/ds/symlink/lm741.pdf",
            "Classic 741 compensated op-amp (MXR Distortion+, DOD 250, 0.5V/us slew)", ("Aol", 100000.0), ("GbwHz", 1000000.0), ("SlewRateVus", 0.50), ("VrailPos", 7.5), ("VrailNeg", 1.5), ("Rin", 2.0e6), ("Rout", 75.0));
        Add("JRC4558", "opamp", "N", "https://www.nisshinbo-microdevices.co.jp/en/pdf/datasheet/NJM4558_E.pdf",
            "Dual bipolar op-amp (TS808, SD-1, OD-1)", ("Aol", 100000.0), ("GbwHz", 3000000.0), ("SlewRateVus", 1.00), ("VrailPos", 7.6), ("VrailNeg", 1.4), ("Rin", 5.0e6), ("Rout", 75.0));
        Add("TL072", "opamp", "N", "https://www.ti.com/lit/ds/symlink/tl072.pdf",
            "Low-noise JFET-input dual op-amp (Klon, Bluesbreaker, Guv'nor)", ("Aol", 100000.0), ("GbwHz", 3000000.0), ("SlewRateVus", 13.0), ("VrailPos", 7.8), ("VrailNeg", 1.2), ("Rin", 1.0e12), ("Rout", 60.0));
        Add("NE5532", "opamp", "N", "https://www.ti.com/lit/ds/symlink/ne5532.pdf",
            "Ultra-low-noise audio op-amp (9V/us slew)", ("Aol", 100000.0), ("GbwHz", 10000000.0), ("SlewRateVus", 9.0), ("VrailPos", 7.6), ("VrailNeg", 1.4), ("Rin", 3.0e5), ("Rout", 50.0));
        Add("OP07", "opamp", "N", "https://www.analog.com/media/en/technical-documentation/data-sheets/OP07.pdf",
            "Modern precision low-slew op-amp (ProCo RAT 2)", ("Aol", 200000.0), ("GbwHz", 600000.0), ("SlewRateVus", 0.30), ("VrailPos", 7.5), ("VrailNeg", 1.5), ("Rin", 5.0e7), ("Rout", 60.0));

        // ── Triodes (Norman Koren Physical Plate Models) ──
        Add("12AX7", "triode", "N", "https://www.jj-electronic.com/images/stories/product/preamplifying_tubes/pdf/ecc83s.pdf",
            "High-mu dual triode ECC83 (Marshall, Fender, Mesa preamps)", ("Mu", 100.0), ("Ex", 1.40), ("Kg1", 1060.0), ("Kp", 600.0), ("Kvb", 300.0), ("RgGrid", 1000.0), ("VgkClip", 0.0));
        Add("12AY7", "triode", "N", "https://www.radiomuseum.org/tubes/tube_12ay7.html",
            "Low-gain tweed preamp triode (Fender 5F6-A Bassman V1)", ("Mu", 44.0), ("Ex", 1.35), ("Kg1", 1350.0), ("Kp", 300.0), ("Kvb", 300.0), ("RgGrid", 1000.0), ("VgkClip", 0.0));
        Add("12AT7", "triode", "N", "https://www.jj-electronic.com/images/stories/product/preamplifying_tubes/pdf/ecc81.pdf",
            "Medium-mu driver/reverb/PI triode ECC81", ("Mu", 60.0), ("Ex", 1.35), ("Kg1", 460.0), ("Kp", 300.0), ("Kvb", 300.0), ("RgGrid", 1000.0), ("VgkClip", 0.0));
    }
}
