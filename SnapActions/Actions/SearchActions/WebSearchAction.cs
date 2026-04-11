using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.SearchActions;

/// <param name="langMode">"url" = {1} in URL, "query" = append lang:xx to query, "" = no lang</param>
public class WebSearchAction(string id, string name, string iconKey, string urlTemplate,
    string lang = "", string langMode = "url") : IAction
{
    public string Id => $"search_{id}";
    public string Name => name;
    public string IconKey => iconKey;
    public ActionCategory Category => ActionCategory.Search;

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text.Trim());

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var query = text.Trim();
        var langCode = lang ?? "";

        // "query" mode: append lang:xx to the search text itself (Twitter style)
        if (langMode == "query" && !string.IsNullOrEmpty(langCode))
            query += $" lang:{langCode}";

        var encoded = Uri.EscapeDataString(query);
        var url = urlTemplate.Replace("{0}", encoded);

        // "url" mode: replace {1} in URL template with language code
        if (langMode == "url" && !string.IsNullOrEmpty(langCode))
        {
            url = url.Replace("{1}", Uri.EscapeDataString(langCode));
        }
        else
        {
            // Remove {1} and clean up empty params
            url = url.Replace("{1}", "");
            url = System.Text.RegularExpressions.Regex.Replace(url, @"[&?][a-z_]+=(?=&|$)", "");
            url = url.TrimEnd('&', '?');
        }

        return ProcessHelper.TryShellOpen(url, $"Searching {name}...");
    }
}
