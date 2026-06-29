using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class SendEmailAction : IAction
{
    public string Id => "send_email";
    public string Name => "Send Email";
    public string IconKey => "IconEmail";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Email;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        // Strip any query / fragment so a sneaky selection like
        // "user@example.com?subject=Phishing&body=Click+here" can't pre-fill the user's mail client.
        var trimmed = text.Trim();
        int q = trimmed.IndexOfAny(['?', '#']);
        if (q >= 0) trimmed = trimmed[..q];
        return ProcessHelper.TryShellOpen($"mailto:{trimmed}", "Email client opened");
    }
}
