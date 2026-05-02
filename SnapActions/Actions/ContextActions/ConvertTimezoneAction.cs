using System.Globalization;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class ConvertTimezoneAction : IAction
{
    public string Id => "convert_timezone";
    public string Name => "Convert Time";
    public string IconKey => "IconTime";
    public ActionCategory Category => ActionCategory.Context;
    // Pure: parses the date and formats Local/UTC/Unix without I/O.
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.DateTime;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        // Compact one-line representation so pastes land cleanly in any single-line context.
        // The previous multi-line "Local:\nUTC:\nUnix:" block was awkward to paste anywhere.
        if (analysis.Metadata?.TryGetValue("parsed", out var parsed) == true &&
            DateTimeOffset.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            var local = dto.ToLocalTime();
            var utc = dto.ToUniversalTime();
            var unix = dto.ToUnixTimeSeconds();
            var result = $"Local: {local:yyyy-MM-dd HH:mm:ss} | UTC: {utc:yyyy-MM-dd HH:mm:ss} | Unix: {unix}";
            return new ActionResult(true, result, "Time converted");
        }

        if (DateTime.TryParse(text.Trim(), out var dt))
        {
            var utc = dt.ToUniversalTime();
            var unix = new DateTimeOffset(dt).ToUnixTimeSeconds();
            var result = $"UTC: {utc:yyyy-MM-dd HH:mm:ss} | Unix: {unix}";
            return new ActionResult(true, result, "Time converted");
        }

        return new ActionResult(false, Message: "Could not parse date/time");
    }
}
