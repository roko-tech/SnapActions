using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapActions.UI;

public partial class QrCodeWindow : Window
{
    private readonly byte[] _png;
    internal QrCodeWindow(string text)
    {
        _png = Services.LocalQr.Encode(text);
        InitializeComponent();
        using var stream = new MemoryStream(_png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        QrImage.Source = bitmap;
        PayloadText.Text = text;
        PayloadText.FlowDirection = ToolbarWindow.GetPreviewFlowDirection(text);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage((BitmapSource)QrImage.Source); }
        catch { MessageBox.Show(this, "The clipboard is busy. Try again.", "QR code"); }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image|*.png", FileName = "qr-code.png" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, _png); }
        catch { MessageBox.Show(this, "The image could not be saved. Choose another location.", "QR code"); }
    }
}
