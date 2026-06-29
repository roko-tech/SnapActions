using System.Net.Http;
using System.Text.Json;
using SnapActions.Config;
using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.UserActions;

/// <summary>
/// A user-defined "recipe" action, built entirely from a <see cref="UserAction"/> settings record —
/// the same data-driven pattern the search engines use, generalized to any selection. OpenUrl
/// launches a templated URL in the browser; FetchText GETs the URL and shows the response (or one
/// JSON field) in a result popup. {0} in the template is the URL-encoded selection.
/// </summary>
public class UserRecipeAction(UserAction def) : IAction
{
    public string Id => $"user_{def.Id}";
    public string Name => def.Name;
    public string IconKey => "IconContext";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (string.IsNullOrEmpty(def.AppliesToType)) return true; // applies to any selection
        return Enum.TryParse<TextType>(def.AppliesToType, ignoreCase: true, out var t) && analysis.Type == t;
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var url = def.UrlTemplate.Replace("{0}", Uri.EscapeDataString(text.Trim()));
        if (def.Kind == UserActionKind.OpenUrl)
            // TryShellOpen enforces the scheme allow-list (http/https/ftp/ftps/mailto), so a recipe
            // can't be used to launch a dangerous custom protocol.
            return ProcessHelper.TryShellOpen(url, $"{def.Name} opened");

        // FetchText: routed through ResultPopup, which applies the online-lookup consent gate before
        // anything leaves the machine.
        var field = def.JsonField;
        UI.ResultPopup.ShowNearCursor(def.Name, (http, ct) => Fetch(http, url, field, ct));
        return new ActionResult(true);
    }

    internal static async Task<string> Fetch(HttpClient http, string url, string jsonField,
        System.Threading.CancellationToken ct)
    {
        string body;
        try { body = await http.GetStringAsync(url, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warn($"User action fetch failed: {ex.Message}"); return "Request failed"; }

        return ExtractField(body, jsonField);
    }

    /// <summary>
    /// Returns the raw body (truncated) when <paramref name="jsonField"/> is empty, otherwise walks
    /// the dotted path into the JSON response. Pure — separated from the HTTP call so it's testable.
    /// </summary>
    internal static string ExtractField(string body, string jsonField)
    {
        if (string.IsNullOrWhiteSpace(jsonField))
            return body.Length > 4000 ? body[..4000] : body;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var el = doc.RootElement;
            foreach (var part in jsonField.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(part, out var next))
                    el = next;
                else
                    return "(field not found)";
            }
            return el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : el.ToString();
        }
        catch { return "(invalid JSON response)"; }
    }
}
