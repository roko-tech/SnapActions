using System.Windows;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class PreviewColorAction : IAction
{
    public string Id => "preview_color";
    public string Name => "Preview Color";
    public string IconKey => "IconColor";
    public ActionCategory Category => ActionCategory.Context;

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
            var (r, g, b) = format switch
            {
                "hex" => HexToRgb(trimmed),
                "rgb" => ParseRgb(trimmed),
                "hsl" => HslToRgb(trimmed),
                _ => throw new FormatException("Unknown color format")
            };

            // Cycle to the next representation.
            var output = format switch
            {
                "hex" => $"rgb({r}, {g}, {b})",
                "rgb" => RgbToHsl(r, g, b),
                _ => $"#{r:X2}{g:X2}{b:X2}"
            };
            return new ActionResult(true, output, $"Converted: {output}");
        }
        catch (Exception ex)
        {
            return new ActionResult(false, Message: ex.Message);
        }
    }

    private static (int r, int g, int b) HexToRgb(string s)
    {
        var hex = s.TrimStart('#');
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length == 4) // #RGBA — short form with alpha; CSS spec
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length < 6) throw new FormatException("Invalid hex color");
        // Both 6 (RRGGBB) and 8 (RRGGBBAA per CSS) take the first 6 chars as RGB.
        // Alpha (positions 6-7 in 8-char form) is intentionally discarded.
        return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
    }

    private static (int r, int g, int b) ParseRgb(string s)
    {
        var nums = System.Text.RegularExpressions.Regex.Matches(s, @"\d+");
        if (nums.Count < 3) throw new FormatException("Invalid rgb()");
        return (int.Parse(nums[0].Value), int.Parse(nums[1].Value), int.Parse(nums[2].Value));
    }

    private static (int r, int g, int b) HslToRgb(string s)
    {
        var nums = System.Text.RegularExpressions.Regex.Matches(s, @"[\d.]+");
        if (nums.Count < 3) throw new FormatException("Invalid hsl()");
        double h = double.Parse(nums[0].Value, System.Globalization.CultureInfo.InvariantCulture);
        double sat = double.Parse(nums[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0;
        double l = double.Parse(nums[2].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0;

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
            (int)Math.Round((b1 + m) * 255, MidpointRounding.AwayFromZero));
    }

    private static string RgbToHsl(int r, int g, int b)
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
        return $"hsl({Math.Round(h, MidpointRounding.AwayFromZero):0}, {Math.Round(s * 100, MidpointRounding.AwayFromZero):0}%, {Math.Round(l * 100, MidpointRounding.AwayFromZero):0}%)";
    }
}
