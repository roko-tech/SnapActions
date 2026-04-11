using System.Text.RegularExpressions;
using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.SearchActions;

public partial class WebSearchAction(string id, string name, string iconKey, string urlTemplate,
    string lang = "", string langMode = "url") : IAction
{
    public string Id => $"search_{id}";
    public string Name => name;
    public string IconKey => iconKey;
    public ActionCategory Category => ActionCategory.Search;

    [GeneratedRegex(@"[&?][a-z_]+=(?=&|$)")]
    private static partial Regex EmptyParamRegex();

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text.Trim());

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var query = text.Trim();
        var langCode = lang ?? "";

        if (langMode == "query" && !string.IsNullOrEmpty(langCode))
            query += $" lang:{langCode}";

        var encoded = Uri.EscapeDataString(query);
        var url = urlTemplate.Replace("{0}", encoded);

        if (langMode == "url" && !string.IsNullOrEmpty(langCode))
        {
            url = url.Replace("{1}", Uri.EscapeDataString(langCode));
        }
        else
        {
            url = url.Replace("{1}", "");
            url = EmptyParamRegex().Replace(url, "");
            url = url.TrimEnd('&', '?');
        }

        return ProcessHelper.TryShellOpen(url, $"Searching {name}...");
    }
}
