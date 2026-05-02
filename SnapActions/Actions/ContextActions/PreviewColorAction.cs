using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class PreviewColorAction : IAction
{
    public string Id => "preview_color";
    public string Name => "Preview Color";
    public string IconKey => "IconColor";
    public ActionCategory Category => ActionCategory.Context;
    // Pure: just returns a Message describing the color, no I/O. Marking preview-safe lets the
    // hover band show a swatch + the color text.
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.ColorCode;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        // Don't write to clipboard — that surprised users when "Preview" silently overwrote their copy buffer.
        // The toolbar's main Copy button is one click away if they actually want it copied.
        return new ActionResult(true, ResultText: null, Message: $"Color: {text.Trim()}");
    }
}

/// <summary>
/// Cycles a color between hex / rgb / hsl. Output format is the *next* one in the cycle.
/// hex → rgb → hsl → hex.
/// </summary>
public class ConvertColorAction : IAction
{
    public string Id => "convert_color";
    public string Name => "Convert Color";
    public string IconKey => "IconConvert";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.ColorCode;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var trimmed = text.Trim();
        var format = analysis.Metadata?.GetValueOrDefault("format") ?? "";
        try
        {
            var (r, g, b, a) = format switch
            {
                "hex" => HexToRgba(trimmed),
                "rgb" => ParseRgba(trimmed),
                "hsl" => HslToRgba(trimmed),
                _ => throw new FormatException("Unknown color format")
            };

            // Cycle to the next representation, preserving alpha when present.
            var output = format switch
            {
                "hex" => FormatRgb(r, g, b, a),
                "rgb" => FormatHsl(r, g, b, a),
                _ => FormatHex(r, g, b, a)
            };
            return new ActionResult(true, output, $"Converted: {output}");
        }
        catch (Exception ex)
        {
            return new ActionResult(false, Message: ex.Message);
        }
    }

    private static string FormatAlpha(double a) =>
        a.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatRgb(int r, int g, int b, double? a) =>
        a.HasValue
            ? $"rgba({r}, {g}, {b}, {FormatAlpha(a.Value)})"
            : $"rgb({r}, {g}, {b})";

    private static string FormatHex(int r, int g, int b, double? a)
    {
        var rgb = $"#{r:X2}{g:X2}{b:X2}";
        if (!a.HasValue) return rgb;
        int ai = (int)Math.Round(a.Value * 255, MidpointRounding.AwayFromZero);
        ai = Math.Clamp(ai, 0, 255);
        return $"{rgb}{ai:X2}";
    }

    private static (int r, int g, int b, double? a) HexToRgba(string s)
    {
        var hex = s.TrimStart('#');
        // Expand short forms. #RGB → #RRGGBB, #RGBA → #RRGGBBAA.
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        else if (hex.Length == 4)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}";
        if (hex.Length < 6) throw new FormatException("Invalid hex color");
        int r = Convert.ToInt32(hex[..2], 16);
        int g = Convert.ToInt32(hex[2..4], 16);
        int b = Convert.ToInt32(hex[4..6], 16);
        double? a = null;
        if (hex.Length >= 8) a = Convert.ToInt32(hex[6..8], 16) / 255.0;
        return (r, g, b, a);
    }

    private static (int r, int g, int b, double? a) ParseRgba(string s)
    {
        // Match each numeric group together with its leading separator so we can tell whether the
        // 4th number was the alpha (preceded by `,` or `/`) or just trailing junk we should ignore.
        var nums = System.Text.RegularExpressions.Regex
            .Matches(s, @"[\d.]+%?")
            .Cast<System.Text.RegularExpressions.Match>()
            .ToList();
        if (nums.Count < 3) throw new FormatException("Invalid rgb()");
        // Clamp to the 0–255 range. The detector regex permits up to 999, so a malformed
        // selection like "rgb(999, 0, 0)" reaches here; without clamping the subsequent
        // FormatHex would output too many hex digits and produce a malformed color string.
        int r = Math.Clamp(int.Parse(nums[0].Value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture), 0, 255);
        int g = Math.Clamp(int.Parse(nums[1].Value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture), 0, 255);
        int b = Math.Clamp(int.Parse(nums[2].Value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture), 0, 255);
        double? a = null;
        if (nums.Count >= 4)
        {
            var raw = nums[3].Value;
            double v = double.Parse(raw.TrimEnd('%'),
                System.Globalization.CultureInfo.InvariantCulture);
            // Percent form (50%) means 0.5; 0–1 numeric stays as is.
            a = raw.EndsWith('%') ? v / 100.0 : v;
        }
        return (r, g, b, a);
    }

    private static (int r, int g, int b, double? a) HslToRgba(string s)
    {
        // Allow a leading minus for hue. The post-parse normalization wraps it back into
        // [0, 360); without "-?" here we'd silently parse "-30" as 30.
        var nums = System.Text.RegularExpressions.Regex
            .Matches(s, @"-?[\d.]+%?")
            .Cast<System.Text.RegularExpressions.Match>()
            .ToList();
        if (nums.Count < 3) throw new FormatException("Invalid hsl()");
        double h = double.Parse(nums[0].Value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
        double sat = double.Parse(nums[1].Value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture) / 100.0;
        double l = double.Parse(nums[2].Value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture) / 100.0;
        double? a = null;
        if (nums.Count >= 4)
        {
            var raw = nums[3].Value;
            double v = double.Parse(raw.TrimEnd('%'),
                System.Globalization.CultureInfo.InvariantCulture);
            a = raw.EndsWith('%') ? v / 100.0 : v;
        }

        // Normalize hue into [0, 360). CSS allows angles outside this range (negative, >360),
        // and without normalization the switch below misclassifies them — h=400 used to fall
        // through to the 300-360 branch, painting orange as something else entirely.
        h = ((h % 360.0) + 360.0) % 360.0;

        double c = (1 - Math.Abs(2 * l - 1)) * sat;
        double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
        double m = l - c / 2;
        (double r1, double g1, double b1) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return (
            (int)Math.Round((r1 + m) * 255, MidpointRounding.AwayFromZero),
            (int)Math.Round((g1 + m) * 255, MidpointRounding.AwayFromZero),
            (int)Math.Round((b1 + m) * 255, MidpointRounding.AwayFromZero),
            a);
    }

    private static string FormatHsl(int r, int g, int b, double? a)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double l = (max + min) / 2;
        double s = 0, h = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == rd) h = (gd - bd) / d + (gd < bd ? 6 : 0);
            else if (max == gd) h = (bd - rd) / d + 2;
            else h = (rd - gd) / d + 4;
            h *= 60;
        }
        var hVal = Math.Round(h, MidpointRounding.AwayFromZero);
        var sPct = Math.Round(s * 100, MidpointRounding.AwayFromZero);
        var lPct = Math.Round(l * 100, MidpointRounding.AwayFromZero);
        return a.HasValue
            ? $"hsla({hVal:0}, {sPct:0}%, {lPct:0}%, {FormatAlpha(a.Value)})"
            : $"hsl({hVal:0}, {sPct:0}%, {lPct:0}%)";
    }
}
