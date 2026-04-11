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
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        return ProcessHelper.TryShellOpen(url, "Opened in browser");
    }
}
