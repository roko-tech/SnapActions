using System.Text;
using QRCoder;

namespace SnapActions.Services;

internal static class LocalQr
{
    internal const int MaximumBytes = 2000;
    internal static byte[] Encode(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaximumBytes)
            throw new ArgumentException("QR text must contain 1–2000 UTF-8 bytes.", nameof(text));
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M, forceUtf8: true, utf8BOM: false,
            eciMode: QRCodeGenerator.EciMode.Utf8);
        using var renderer = new PngByteQRCode(data);
        return renderer.GetGraphic(6);
    }
}
