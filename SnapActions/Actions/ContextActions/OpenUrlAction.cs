using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class OpenUrlAction : IAction
{
    public string Id => "open_url";
    public string Name => "Open URL";
    public string IconKey => "IconOpenUrl";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Url;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var url = text.Trim();
        // Don't blindly prepend https:// — the detector also accepts ftp:// and bare www.host.
        // Only prepend when the selection has no scheme of its own.
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        return ProcessHelper.TryShellOpen(url, "Opened in browser");
    }
}
