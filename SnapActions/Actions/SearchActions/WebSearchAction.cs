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

    [GeneratedRegex(@"://\{1\}\.")]
    private static partial Regex HostPlaceholderRegex();

    // Matches a query param whose value still contains {1} (drops the whole param when lang is empty).
    [GeneratedRegex(@"[&?][^&=]+=[^&]*\{1\}[^&]*")]
    private static partial Regex ParamWithLangPlaceholderRegex();

    // Matches an empty-valued param (e.g. "&hl=" at end or before another &).
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
            // Fallback for host-position substitutions like https://{1}.wikipedia.org/...
            // Empty {1} would yield https://.wikipedia.org/...
            url = HostPlaceholderRegex().Replace(url, "://en.");
            // Drop entire query params that still contain {1} (e.g. &lr=lang_{1}).
            url = ParamWithLangPlaceholderRegex().Replace(url, "");
            // Any stray {1} elsewhere just becomes empty.
            url = url.Replace("{1}", "");
            // Trailing empty-valued params get cleaned up too.
            url = EmptyParamRegex().Replace(url, "");
            url = url.TrimEnd('&', '?');
        }

        return ProcessHelper.TryShellOpen(url, $"Searching {name}...");
    }
}
