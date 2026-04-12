using SnapActions.Actions.ContextActions;
using SnapActions.Actions.TransformActions;
using SnapActions.Detection;

namespace SnapActions.Actions;

public class ActionRegistry
{
    private readonly List<IAction> _allActions;

    public ActionRegistry()
    {
        _allActions =
        [
            // Context actions
            new OpenUrlAction(),
            new SendEmailAction(),
            new OpenFilePathAction(),
            new OpenContainingFolderAction(),
            new PreviewColorAction(),
            new ConvertColorAction(),
            new FormatJsonAction(),
            new MinifyJsonAction(),
            new FormatXmlAction(),
            new StripTagsAction(),
            new CalculateAction(),
            new IpLookupAction(),
            new DecodeBase64Action(),
            new GenerateUuidAction(),
            new ConvertTimezoneAction(),
            new TranslateAction(),
            new DictionaryAction(),
            new SpellCheckAction(),
            new CurrencyConverterAction(),
            new DeleteTextAction(),
            new PastePlainTextAction(),

            // Transform actions
            new CaseTransformAction("upper", "UPPERCASE", "IconUppercase", text => text.ToUpperInvariant()),
            new CaseTransformAction("lower", "lowercase", "IconLowercase", text => text.ToLowerInvariant()),
            new CaseTransformAction("title", "Title Case", "IconTitleCase", ToTitleCase),
            new CaseTransformAction("camel", "camelCase", "IconCamelCase", ToCamelCase),
            new CaseTransformAction("snake", "snake_case", "IconSnakeCase", ToSnakeCase),
            new CaseTransformAction("kebab", "kebab-case", "IconKebabCase", ToKebabCase),
            new CaseTransformAction("pascal", "PascalCase", "IconPascalCase", ToPascalCase),
            new CaseTransformAction("reverse", "Reverse", "IconReverse", text => new string(text.Reverse().ToArray())),

            new WhitespaceAction("trim", "Trim", text => text.Trim()),
            new WhitespaceAction("remove_extra_spaces", "Remove Extra Spaces",
                text => System.Text.RegularExpressions.Regex.Replace(text, @" {2,}", " ")),
            new WhitespaceAction("sort_lines", "Sort Lines",
                text => string.Join("\n", text.Split('\n').Order())),
            new WhitespaceAction("dedup_lines", "Remove Duplicates",
                text => string.Join("\n", text.Split('\n').Distinct())),
            new WhitespaceAction("remove_linebreaks", "Remove Line Breaks",
                text => System.Text.RegularExpressions.Regex.Replace(text, @"[\r\n]+", " ").Trim()),

            // Encoding actions
            new EncodingAction("url_encode", "URL Encode", "IconEncode",
                text => Uri.EscapeDataString(text)),
            new EncodingAction("url_decode", "URL Decode", "IconDecode",
                text => Uri.UnescapeDataString(text)),
            new EncodingAction("base64_encode", "Base64 Encode", "IconEncode",
                text => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))),
            new EncodingAction("base64_decode", "Base64 Decode", "IconDecode",
                text => { try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text)); } catch { return "[Invalid Base64]"; } }),
            new EncodingAction("html_encode", "HTML Encode", "IconEncode",
                text => System.Net.WebUtility.HtmlEncode(text)),
            new EncodingAction("html_decode", "HTML Decode", "IconDecode",
                text => System.Net.WebUtility.HtmlDecode(text)),

            // Wrap actions
            new WrapAction("wrap_quotes", "Wrap \"quotes\"", "\"", "\""),
            new WrapAction("wrap_single_quotes", "Wrap 'quotes'", "'", "'"),
            new WrapAction("wrap_parens", "Wrap (parens)", "(", ")"),
            new WrapAction("wrap_brackets", "Wrap [brackets]", "[", "]"),
            new WrapAction("wrap_braces", "Wrap {braces}", "{", "}"),
            new WrapAction("wrap_backticks", "Wrap `backticks`", "`", "`"),

        ];
    }

    public List<ActionGroup> GetActions(string text, TextAnalysis analysis)
    {
        var s = Config.SettingsManager.Current;
        var groups = new List<ActionGroup>();
        var disabled = s.DisabledActionIds;
        var applicable = _allActions
            .Where(a => a.CanExecute(text, analysis) && !disabled.Contains(a.Id))
            .ToList();

        var contextActions = applicable.Where(a => a.Category == ActionCategory.Context).ToList();
        if (contextActions.Count > 0)
            groups.Add(new ActionGroup("Context", "IconContext", contextActions));

        if (s.ShowTransformActions)
        {
            var list = applicable.Where(a => a.Category == ActionCategory.Transform).ToList();
            if (list.Count > 0) groups.Add(new ActionGroup("Transform", "IconTransform", list));
        }
        if (s.ShowEncodeActions)
        {
            var list = applicable.Where(a => a.Category == ActionCategory.Encode).ToList();
            if (list.Count > 0) groups.Add(new ActionGroup("Encode", "IconEncode", list));
        }
        if (s.ShowSearchActions && !string.IsNullOrEmpty(text.Trim()))
        {
            var lang = s.SearchLanguage ?? "";
            var searchActions = s.SearchEngines
                .Where(e => e.Enabled)
                .Select(e => (IAction)new SearchActions.WebSearchAction(
                    e.Id, e.Name, "IconSearch", e.UrlTemplate, lang, e.LangMode))
                .ToList();
            if (searchActions.Count > 0)
                groups.Add(new ActionGroup("Search", "IconSearch", searchActions));
        }

        return groups;
    }

    /// <summary>Get all actions for a category (including disabled ones) for the edit mode UI.</summary>
    public List<IAction> GetAllActionsForCategory(ActionCategory category)
    {
        if (category == ActionCategory.Search)
        {
            // Search actions are built from settings, not from _allActions
            var lang = Config.SettingsManager.Current.SearchLanguage ?? "";
            return Config.SettingsManager.Current.SearchEngines
                .Select(e => (IAction)new SearchActions.WebSearchAction(e.Id, e.Name, "IconSearch", e.UrlTemplate, lang, e.LangMode))
                .ToList();
        }
        return _allActions.Where(a => a.Category == category).ToList();
    }

    // Text transformation helpers
    private static string ToTitleCase(string text) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());

    private static string ToCamelCase(string text)
    {
        var words = SplitWords(text);
        if (words.Length == 0) return text;
        return words[0].ToLower() + string.Concat(words.Skip(1).Select(w =>
            char.ToUpper(w[0]) + w[1..].ToLower()));
    }

    private static string ToPascalCase(string text)
    {
        var words = SplitWords(text);
        return string.Concat(words.Select(w => char.ToUpper(w[0]) + w[1..].ToLower()));
    }

    private static string ToSnakeCase(string text) =>
        string.Join('_', SplitWords(text).Select(w => w.ToLower()));

    private static string ToKebabCase(string text) =>
        string.Join('-', SplitWords(text).Select(w => w.ToLower()));

    private static string[] SplitWords(string text)
    {
        // Split on spaces, underscores, hyphens, and camelCase boundaries
        var result = new List<string>();
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ' || c == '_' || c == '-' || c == '.')
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else if (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1]))
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                current.Append(c);
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result.Where(w => w.Length > 0).ToArray();
    }
}

public record ActionGroup(string Name, string IconKey, List<IAction> Actions);
