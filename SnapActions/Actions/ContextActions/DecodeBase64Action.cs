using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class DecodeBase64Action : IAction
{
    public string Id => "decode_base64";
    public string Name => "Decode Base64";
    public string IconKey => "IconDecode";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Base64;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        try
        {
            var bytes = Convert.FromBase64String(text.Trim());
            var decoded = System.Text.Encoding.UTF8.GetString(bytes);
            return new ActionResult(true, decoded, "Base64 decoded");
        }
        catch
        {
            return new ActionResult(false, Message: "Invalid Base64");
        }
    }
}
