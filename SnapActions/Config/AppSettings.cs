namespace SnapActions.Config;

public class SearchEngine
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// URL template. {0} = URL-encoded query. {1} = language code (e.g. "en", "ar", "ja").
    /// Example: "https://www.google.com/search?q={0}&hl={1}"
    /// </summary>
    public string UrlTemplate { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    /// <summary>"url" = {1} in URL, "query" = append lang:xx to search text, "" = no lang support</summary>
    public string LangMode { get; set; } = "url";
}

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool AutoStart { get; set; } = false;
    public bool Enabled { get; set; } = true;
    public int ToolbarDismissTimeout { get; set; } = 8000;
    /// <summary>Delay in ms before showing toolbar after selection (0 = instant).</summary>
    public int ToolbarShowDelay { get; set; } = 0;
    public List<string> ExcludedApps { get; set; } = ["KeePass", "1Password", "Bitwarden"];
    public bool ReplaceSelectionOnTransform { get; set; } = true;

    public bool ShowTransformActions { get; set; } = true;
    public bool ShowEncodeActions { get; set; } = true;
    public bool ShowSearchActions { get; set; } = true;

    /// <summary>Language code for search filtering (e.g. "en", "ar", "ja", ""). Empty = no filter.</summary>
    public string SearchLanguage { get; set; } = "";

    public List<SearchEngine> SearchEngines { get; set; } = GetDefaultEngines();

    public List<string> DisabledActionIds { get; set; } = [];

    /// <summary>Action IDs pinned to the main toolbar bar.</summary>
    public List<string> PinnedActionIds { get; set; } = [];

    public static List<SearchEngine> GetDefaultEngines() =>
    [
        new() { Id = "google", Name = "Google", IsBuiltIn = true,
            UrlTemplate = "https://www.google.com/search?q={0}&lr=lang_{1}&hl={1}" },
        new() { Id = "bing", Name = "Bing", IsBuiltIn = true,
            UrlTemplate = "https://www.bing.com/search?q={0}&setlang={1}" },
        new() { Id = "duckduckgo", Name = "DuckDuckGo", IsBuiltIn = true,
            UrlTemplate = "https://duckduckgo.com/?q={0}", LangMode = "" },
        new() { Id = "youtube", Name = "YouTube", IsBuiltIn = true,
            UrlTemplate = "https://www.youtube.com/results?search_query={0}&hl={1}" },
        new() { Id = "twitter", Name = "Twitter/X", IsBuiltIn = true,
            UrlTemplate = "https://x.com/search?q={0}&f=top", LangMode = "query" },
        new() { Id = "reddit", Name = "Reddit", IsBuiltIn = true,
            UrlTemplate = "https://www.reddit.com/search/?q={0}", LangMode = "" },
        new() { Id = "github", Name = "GitHub", IsBuiltIn = true,
            UrlTemplate = "https://github.com/search?q={0}&type=code", LangMode = "" },
        new() { Id = "stackoverflow", Name = "StackOverflow", IsBuiltIn = true,
            UrlTemplate = "https://stackoverflow.com/search?q={0}", LangMode = "" },
        new() { Id = "wikipedia", Name = "Wikipedia", IsBuiltIn = true,
            UrlTemplate = "https://{1}.wikipedia.org/w/index.php?search={0}" },
        new() { Id = "amazon", Name = "Amazon", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.amazon.com/s?k={0}" },
        new() { Id = "imdb", Name = "IMDb", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.imdb.com/find/?q={0}" },
        new() { Id = "npm", Name = "npm", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.npmjs.com/search?q={0}" },
        new() { Id = "nuget", Name = "NuGet", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.nuget.org/packages?q={0}" },
    ];
}
