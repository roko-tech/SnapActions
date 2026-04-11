using System.Globalization;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class ConvertTimezoneAction : IAction
{
    public string Id => "convert_timezone";
    public string Name => "Convert Time";
    public string IconKey => "IconTime";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.DateTime;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        if (analysis.Metadata?.TryGetValue("parsed", out var parsed) == true &&
            DateTimeOffset.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            var local = dto.ToLocalTime();
            var utc = dto.ToUniversalTime();
            var unix = dto.ToUnixTimeSeconds();
            var result = $"Local: {local:yyyy-MM-dd HH:mm:ss}\nUTC: {utc:yyyy-MM-dd HH:mm:ss}\nUnix: {unix}";
            return new ActionResult(true, result, "Time converted");
        }

        if (DateTime.TryParse(text.Trim(), out var dt))
        {
            var utc = dt.ToUniversalTime();
            var unix = new DateTimeOffset(dt).ToUnixTimeSeconds();
            var result = $"UTC: {utc:yyyy-MM-dd HH:mm:ss}\nUnix: {unix}";
            return new ActionResult(true, result, "Time converted");
        }

        return new ActionResult(false, Message: "Could not parse date/time");
    }
}
