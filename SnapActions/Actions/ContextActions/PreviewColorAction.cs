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
        // Copy color to clipboard and show info
        Clipboard.SetText(text.Trim());
        return new ActionResult(true, text.Trim(), $"Color: {text.Trim()} (copied)");
    }
}

public class ConvertColorAction : IAction
{
    public string Id => "convert_color";
    public string Name => "Convert Color";
    public string IconKey => "IconConvert";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) =>
        analysis.Type == TextType.ColorCode && analysis.Metadata?["format"] == "hex";

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";

        if (hex.Length >= 6)
        {
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            var rgb = $"rgb({r}, {g}, {b})";
            return new ActionResult(true, rgb, $"Converted: {rgb}");
        }
        return new ActionResult(false, Message: "Invalid color");
    }
}
