using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class GenerateQrAction : IAction
{
    public string Id => "generate_qr";
    public string Name => "QR Code";
    public string IconKey => "IconQrCode";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) =>
        analysis.Type == TextType.Url && text.Length <= 900;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var encoded = Uri.EscapeDataString(text.Trim());
        var url = $"https://api.qrserver.com/v1/create-qr-code/?data={encoded}&size=300x300";
        return ProcessHelper.TryShellOpen(url, "QR code opened");
    }
}
