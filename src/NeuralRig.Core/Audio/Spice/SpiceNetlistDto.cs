using System.Globalization;

namespace NeuralRigStudio.Audio.Spice;

/// <summary>
/// MNA SPICE 넷리스트 기반 커스텀 페달/앰프 회로 정의 DTO.
/// </summary>
public sealed class SpiceCircuitDto
{
    public string FormatVersion { get; set; } = "spice-mna-v1";
    public float InputVoltageScale { get; set; } = 0.45f;
    public float OutputVoltageScale { get; set; } = 0f; // 0 = auto (1 / InputVoltageScale)
    public bool OutputDcBlock { get; set; } = true;
    public List<SpiceKnobDefDto> Knobs { get; set; } = new();
    public List<SpiceStageDto> Stages { get; set; } = new();
    public Dictionary<string, SpiceComponentModelDto>? InlineModels { get; set; }
}

public sealed class SpiceKnobDefDto
{
    public int Index { get; set; } // 0..7
    public string Label { get; set; } = "KNOB";
    public float Min { get; set; } = 0f;
    public float Max { get; set; } = 10f;
    public float Default { get; set; } = 5f;
    public string Unit { get; set; } = "";
    public bool IsSwitch { get; set; }
}

public sealed class SpiceStageDto
{
    public string Name { get; set; } = "stage1";
    public string InputNode { get; set; } = "in";
    public string OutputNode { get; set; } = "out";
    public float InputSeriesResistance { get; set; } = 1000f; // <= 0 uses ideal Dirichlet voltage source at InputNode
    public List<SpiceFixedVoltageDto> Supplies { get; set; } = new();
    public List<SpiceElementDto> Elements { get; set; } = new();
}

public sealed class SpiceFixedVoltageDto
{
    public string Node { get; set; } = "vcc";
    public double Voltage { get; set; } = 9.0;
}

public sealed class SpiceElementDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "R"; // R | C | L | POT | SW | D | Q | J | M | U | T
    public string[] Nodes { get; set; } = Array.Empty<string>();
    public double? Value { get; set; }
    public string? ValueText { get; set; }
    public string? Model { get; set; }

    // Potentiometer / Switch options
    public int KnobIndex { get; set; } = 0; // 0..7
    public string Taper { get; set; } = "LIN"; // LIN | LOG | RLOG
    public bool Reversed { get; set; }

    // Switch threshold / resistances
    public double Threshold { get; set; } = 0.5;
    public double ClosedOhms { get; set; } = 0.05;
    public double OpenOhms { get; set; } = 1.0e8;
    public bool Inverted { get; set; }

    // Optional Op-Amp supply rail override (single/dual supply offset)
    public double? VposOverride { get; set; }
    public double? VnegOverride { get; set; }
}

public sealed class FreqResponsePointDto
{
    public float FreqHz { get; set; }
    public float Db { get; set; }
}

/// <summary>
/// 실제 측정 주파수 응답 커브 또는 음향/WAV 스펙 기반 커스텀 캐비넷 IR 정의 DTO.
/// </summary>
public sealed class CustomCabAcousticDto
{
    public string EnclosureType { get; set; } = "4x12 Closed Back";
    public float ResonanceHz { get; set; } = 78f;
    public float HighRollOffHz { get; set; } = 4800f;
    public float ErDelayMs { get; set; } = 1.5f;
    public float ErGain { get; set; } = -0.10f;
    public List<FreqResponsePointDto> MeasuredCurve { get; set; } = new();
    public string? WavIrPath { get; set; }
}

/// <summary>
/// SPICE 및 회로도 표기값(예: "4.7k", "4k7", "47n", "51pF", "1M", "1Meg", "2.2uF", "10mH") 파서.
/// </summary>
public static class SpiceValueParser
{
    public static bool TryParse(double? numericValue, string? valueText, out double result)
    {
        if (numericValue.HasValue && double.IsFinite(numericValue.Value))
        {
            result = numericValue.Value;
            return true;
        }
        if (string.IsNullOrWhiteSpace(valueText))
        {
            result = 0.0;
            return false;
        }

        string s = valueText.Trim();
        // Trailing unit words (do not strip 'm'/'M' before checking suffix!)
        string[] unitTails = { "ohms", "ohm", "hz", "Ω" };
        foreach (var tail in unitTails)
        {
            if (s.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^tail.Length].Trim();
                break;
            }
        }
        // Strip trailing 'F' (farad) or 'H' (henry) when preceded by a metric prefix or digit
        if (s.Length >= 2 && (s[^1] == 'F' || s[^1] == 'f' || s[^1] == 'H' || s[^1] == 'h') && s[^2] != 'e' && s[^2] != 'E')
            s = s[..^1].Trim();

        // Compact European resistor/cap notation e.g. "4k7" -> "4.7k", "2u2" -> "2.2u", "1M5" -> "1.5M"
        s = NormalizeRkmNotation(s);

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double direct) && double.IsFinite(direct))
        {
            result = direct;
            return true;
        }

        // Split numeric prefix from SI suffix
        int split = 0;
        while (split < s.Length)
        {
            char c = s[split];
            if (char.IsDigit(c) || c == '.' || c == '+' || c == '-')
            {
                split++;
                continue;
            }
            if ((c == 'e' || c == 'E') && split + 1 < s.Length &&
                (char.IsDigit(s[split + 1]) || s[split + 1] == '+' || s[split + 1] == '-'))
            {
                split++;
                continue;
            }
            break;
        }

        if (split == 0 || !double.TryParse(s[..split], NumberStyles.Float, CultureInfo.InvariantCulture, out double mantissa))
        {
            result = 0.0;
            return false;
        }

        string suffix = s[split..].Trim();
        double mult;
        if (string.IsNullOrEmpty(suffix))
        {
            mult = 1.0;
        }
        else if (suffix.StartsWith("meg", StringComparison.OrdinalIgnoreCase))
        {
            mult = 1.0e6;
        }
        else
        {
            mult = suffix[0] switch
            {
                'f' => 1.0e-15,
                'p' => 1.0e-12,
                'n' => 1.0e-9,
                'u' or 'U' or 'µ' or 'μ' => 1.0e-6,
                'm' => 1.0e-3,
                'M' => 1.0e6, // Schematic uppercase 'M' = Mega (1M resistor = 1e6)
                'k' or 'K' => 1.0e3,
                'g' or 'G' => 1.0e9,
                'r' or 'R' => 1.0,
                _ => double.NaN,
            };
        }

        if (double.IsNaN(mult))
        {
            result = 0.0;
            return false;
        }
        result = mantissa * mult;
        return double.IsFinite(result);
    }

    private static string NormalizeRkmNotation(string s)
    {
        for (int i = 1; i < s.Length - 1; i++)
        {
            char c = s[i];
            if (c is 'k' or 'K' or 'r' or 'R' or 'M' or 'u' or 'U' or 'n' or 'p'
                && char.IsDigit(s[i - 1])
                && char.IsDigit(s[i + 1]))
            {
                return string.Concat(s.AsSpan(0, i), ".", s.AsSpan(i + 1), c == 'r' || c == 'R' ? "" : c.ToString());
            }
        }
        return s;
    }
}
