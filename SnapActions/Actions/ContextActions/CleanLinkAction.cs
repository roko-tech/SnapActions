using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public sealed class CleanLinkAction : IAction
{
    public string Id => "clean_link";
    public string Name => "Clean tracking link";
    public string IconKey => "IconOpenUrl";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;
    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Url && Clean(text) != text.Trim();
    public ActionResult Execute(string text, TextAnalysis analysis) => new(true, Clean(text), "Tracking parameters removed");

    internal static string Clean(string text)
    {
        var value = text.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return value;
        int question = value.IndexOf('?'), fragment = value.IndexOf('#');
        if (question < 0 || fragment >= 0 && fragment < question) return value;
        int end = fragment < 0 ? value.Length : fragment;
        var kept = value[(question + 1)..end].Split('&').Where(part => !IsTracking(part.Split('=', 2)[0])).ToArray();
        return value[..question] + (kept.Length > 0 ? "?" + string.Join('&', kept) : "") + value[end..];
    }

    private static bool IsTracking(string name)
    {
        name = Uri.UnescapeDataString(name).ToLowerInvariant();
        return name.StartsWith("utm_", StringComparison.Ordinal) || name is
            "fbclid" or "gclid" or "dclid" or "msclkid" or "mc_cid" or "mc_eid" or "igshid" or "_hsenc" or "_hsmi";
    }
}
