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

    public ActionResult Execute(string text, TextAnalysis analysis) =>
        ProcessHelper.TryShellOpen($"mailto:{text.Trim()}", "Email client opened");
}
