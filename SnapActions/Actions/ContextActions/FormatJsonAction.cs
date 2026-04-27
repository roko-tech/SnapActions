using System.Text.Json;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class FormatJsonAction : IAction
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public string Id => "format_json";
    public string Name => "Format JSON";
    public string IconKey => "IconFormatJson";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Json;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        try
        {
            using var doc = JsonDocument.Parse(text.Trim());
            var formatted = JsonSerializer.Serialize(doc, IndentedJson);
            return new ActionResult(true, formatted, "JSON formatted");
        }
        catch
        {
            return new ActionResult(false, Message: "Invalid JSON");
        }
    }
}

public class MinifyJsonAction : IAction
{
    public string Id => "minify_json";
    public string Name => "Minify JSON";
    public string IconKey => "IconMinify";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Json;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        try
        {
            using var doc = JsonDocument.Parse(text.Trim());
            var minified = JsonSerializer.Serialize(doc);
            return new ActionResult(true, minified, "JSON minified");
        }
        catch
        {
            return new ActionResult(false, Message: "Invalid JSON");
        }
    }
}
