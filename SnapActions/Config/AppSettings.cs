namespace SnapActions.Config;

/// <summary>
/// How a search engine consumes the language code.
/// JSON values are kept as the lowercase short string ("url", "query", "none") so existing
/// settings.json files remain readable.
/// </summary>
public enum LangMode
{
    /// <summary>Substitute {1} into the URL template (default).</summary>
    Url,
    /// <summary>Append `lang:xx` to the search text (Twitter/X uses this).</summary>
    Query,
    /// <summary>Engine doesn't accept a language hint.</summary>
    None,
}

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

    /// <summary>How this engine consumes the language code.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(LangModeJsonConverter))]
    public LangMode LangMode { get; set; } = LangMode.Url;

    /// <summary>Whether to apply the global SearchLanguage filter to this engine.</summary>
    public bool UseLanguageFilter { get; set; } = true;
}

/// <summary>
/// Reads the legacy string values ("url"/"query"/""/"none") and writes the lowercase form.
/// Keeps existing user settings.json files compatible across the enum migration.
/// </summary>
public class LangModeJsonConverter : System.Text.Json.Serialization.JsonConverter<LangMode>
{
    public override LangMode Read(ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? "";
        return s.ToLowerInvariant() switch
        {
            "url" => LangMode.Url,
            "query" => LangMode.Query,
            "" or "none" => LangMode.None,
            _ => LangMode.Url, // unknown values fall back to default
        };
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, LangMode value,
        System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            LangMode.Url => "url",
            LangMode.Query => "query",
            _ => "none",
        });
}

public class AppSettings
{
    public bool AutoStart { get; set; } = false;
    public bool Enabled { get; set; } = true;
    public int ToolbarDismissTimeout { get; set; } = 8000;
    /// <summary>Delay in ms before showing toolbar after selection (0 = instant).</summary>
    public int ToolbarShowDelay { get; set; } = 0;
    /// <summary>Delay in ms after double/triple click before firing (allows next click). 0 = instant.</summary>
    public int MultiClickDelay { get; set; } = 200;
    /// <summary>How long the user must hold the left button (ms) before paste mode appears.</summary>
    public int LongPressDuration { get; set; } = 500;
    public List<string> ExcludedApps { get; set; } = [
        "KeePass", "KeePassXC", "1Password", "Bitwarden", "Dashlane", "Enpass", "LastPass",
        "RoboForm", "NordPass", "ProtonPass", "KeeperPasswordManager"
    ];
    public bool ReplaceSelectionOnTransform { get; set; } = true;

    public bool ShowTransformActions { get; set; } = true;
    public bool ShowEncodeActions { get; set; } = true;
    public bool ShowSearchActions { get; set; } = true;

    /// <summary>Language code for search filtering (e.g. "en", "ar", "ja", ""). Empty = no filter.</summary>
    public string SearchLanguage { get; set; } = "";

    public List<SearchEngine> SearchEngines { get; set; } = GetDefaultEngines();

    /// <summary>Target currency for conversion (e.g. "USD", "EUR", "SAR")</summary>
    public string TargetCurrency { get; set; } = "USD";

    public List<string> DisabledActionIds { get; set; } = [];

    /// <summary>Action IDs pinned to the main toolbar bar.</summary>
    public List<string> PinnedActionIds { get; set; } = [];

    /// <summary>How many context-action buttons to show inline on the toolbar (the rest stay in the dropdown).</summary>
    public int MaxInlineContextActions { get; set; } = 4;

    public static List<SearchEngine> GetDefaultEngines() =>
    [
        new() { Id = "google", Name = "Google", IsBuiltIn = true,
            UrlTemplate = "https://www.google.com/search?q={0}&lr=lang_{1}&hl={1}" },
        new() { Id = "bing", Name = "Bing", IsBuiltIn = true,
            UrlTemplate = "https://www.bing.com/search?q={0}&setlang={1}" },
        new() { Id = "duckduckgo", Name = "DuckDuckGo", IsBuiltIn = true,
            UrlTemplate = "https://duckduckgo.com/?q={0}", LangMode = LangMode.None, UseLanguageFilter = false },
        new() { Id = "youtube", Name = "YouTube", IsBuiltIn = true,
            UrlTemplate = "https://www.youtube.com/results?search_query={0}&hl={1}" },
        new() { Id = "twitter", Name = "Twitter/X", IsBuiltIn = true,
            UrlTemplate = "https://x.com/search?q={0}&f=top", LangMode = LangMode.Query },
        new() { Id = "reddit", Name = "Reddit", IsBuiltIn = true,
            UrlTemplate = "https://www.reddit.com/search/?q={0}", LangMode = LangMode.None, UseLanguageFilter = false },
        new() { Id = "github", Name = "GitHub", IsBuiltIn = true,
            UrlTemplate = "https://github.com/search?q={0}&type=code", LangMode = LangMode.None, UseLanguageFilter = false },
        new() { Id = "stackoverflow", Name = "StackOverflow", IsBuiltIn = true,
            UrlTemplate = "https://stackoverflow.com/search?q={0}", LangMode = LangMode.None, UseLanguageFilter = false },
        new() { Id = "wikipedia", Name = "Wikipedia", IsBuiltIn = true,
            UrlTemplate = "https://{1}.wikipedia.org/w/index.php?search={0}" },
        new() { Id = "amazon", Name = "Amazon", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.amazon.com/s?k={0}", UseLanguageFilter = false },
        new() { Id = "imdb", Name = "IMDb", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.imdb.com/find/?q={0}", UseLanguageFilter = false },
        new() { Id = "npm", Name = "npm", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.npmjs.com/search?q={0}", UseLanguageFilter = false },
        new() { Id = "nuget", Name = "NuGet", IsBuiltIn = true, Enabled = false,
            UrlTemplate = "https://www.nuget.org/packages?q={0}", UseLanguageFilter = false },
    ];
}
