using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class IpLookupAction : IAction
{
    public string Id => "ip_lookup";
    public string Name => "IP Lookup";
    public string IconKey => "IconIpLookup";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.IpAddress;

    public ActionResult Execute(string text, TextAnalysis analysis) =>
        ProcessHelper.TryShellOpen(
            $"https://ipinfo.io/{Uri.EscapeDataString(text.Trim())}",
            "IP lookup opened");
}
