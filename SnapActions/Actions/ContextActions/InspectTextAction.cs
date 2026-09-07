using System.Globalization;
using System.Text;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public sealed class InspectTextAction : IAction
{
    public string Id => "inspect_text";
    public string Name => "Inspect text";
    public string IconKey => "IconInfo";
    public ActionCategory Category => ActionCategory.Context;
    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);
    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        UI.ResultPopup.ShowLocalResult(Name, Describe(text));
        return new(true);
    }

    internal static string Describe(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        var output = new StringBuilder()
            .AppendLine($"Text elements: {new StringInfo(text).LengthInTextElements}")
            .AppendLine($"Unicode code points: {runes.Length}")
            .AppendLine($"UTF-16 units: {text.Length} · UTF-8 bytes: {Encoding.UTF8.GetByteCount(text)}")
            .AppendLine($"Lines: {text.Count(c => c == '\n') + 1}").AppendLine();
        foreach (var rune in runes.Take(256))
        {
            string label = rune.Value switch
            {
                9 => "TAB", 10 => "LINE FEED", 13 => "CARRIAGE RETURN", 32 => "SPACE", 160 => "NO-BREAK SPACE",
                0x200B => "ZERO WIDTH SPACE", 0x200C => "ZERO WIDTH NON-JOINER", 0x200D => "ZERO WIDTH JOINER",
                0x200E => "LEFT-TO-RIGHT MARK", 0x200F => "RIGHT-TO-LEFT MARK", 0x061C => "ARABIC LETTER MARK",
                0x202A => "LEFT-TO-RIGHT EMBEDDING", 0x202B => "RIGHT-TO-LEFT EMBEDDING", 0x202C => "POP DIRECTIONAL FORMATTING",
                0x202D => "LEFT-TO-RIGHT OVERRIDE", 0x202E => "RIGHT-TO-LEFT OVERRIDE",
                0x2066 => "LEFT-TO-RIGHT ISOLATE", 0x2067 => "RIGHT-TO-LEFT ISOLATE", 0x2068 => "FIRST STRONG ISOLATE", 0x2069 => "POP DIRECTIONAL ISOLATE",
                _ => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format ? "INVISIBLE CONTROL" : rune.ToString()
            };
            output.AppendLine($"U+{rune.Value:X4}  {label}");
        }
        if (runes.Length > 256) output.AppendLine("Code-point list shows the first 256; counts cover the complete selection.");
        return output.ToString();
    }
}
