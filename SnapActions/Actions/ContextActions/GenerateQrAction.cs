using SnapActions.Detection;
using SnapActions.Services;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public class GenerateQrAction : IAction
{
    public string Id => "generate_qr";
    public string Name => "QR Code";
    public string IconKey => "IconQrCode";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) =>
        !string.IsNullOrWhiteSpace(text) && System.Text.Encoding.UTF8.GetByteCount(text) <= LocalQr.MaximumBytes;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        if (!CanExecute(text, analysis)) return new(false, Message: "QR codes support up to 2,000 UTF-8 bytes.");
        new QrCodeWindow(text).Show();
        return new(true, Message: "QR code generated locally");
    }
}
