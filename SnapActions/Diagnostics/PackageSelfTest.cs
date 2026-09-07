using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapActions.Actions;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.Detection;
using SnapActions.Services;
using SnapActions.UI;
using TextBox = System.Windows.Controls.TextBox;

namespace SnapActions.Diagnostics;

/// <summary>Runs only with --self-test and an isolated data directory; never installs hooks or changes the clipboard.</summary>
internal static class PackageSelfTest
{
    internal static async Task<int> RunAsync()
    {
        if (!RuntimePaths.IsIsolated) return 2;
        var checks = new List<string>();
        string? failure = null;
        Directory.CreateDirectory(RuntimePaths.DataDirectory);
        try
        {
            SettingsManager.Load();
            foreach (string asset in new[] { "manifest.json", "background.js", "read-selection.js", "selection-sample.html", "install-host.ps1" })
                Require(File.Exists(Path.Combine(BrowserSetupService.ExtensionDirectory, asset)), "Missing companion file: " + asset);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(BrowserSetupService.ExtensionDirectory, "manifest.json")));
            Require(manifest.RootElement.GetProperty("minimum_chrome_version").GetString() == "106", "Companion browser minimum mismatch");
            checks.Add("Companion publish assets present");

            foreach (var theme in new[] { "dark", "light" })
            {
                SettingsManager.Current.Theme = theme; ThemeManager.Apply();
                var settings = new SettingsWindow();
                settings.LoadSettings();
                var tabs = (System.Windows.Controls.TabControl)settings.FindName("SettingsTabs");
                foreach (TabItem tab in tabs.Items)
                {
                    tabs.SelectedItem = tab;
                    Render(settings, $"settings-{theme}-{tab.Header}", 692, 644);
                }
                ((TextBox)settings.FindName("SettingsSearchBox")).Text = "translation";
                Require(tabs.Items.Cast<TabItem>().Any(t => t.Visibility == Visibility.Visible && t.Header.ToString() == "Languages"), "Settings search lost translation section");
                ((TextBox)settings.FindName("SettingsSearchBox")).Text = "";
                tabs.SelectedIndex = 0; Render(settings, $"settings-{theme}-small", 572, 404);
                settings.Close();
                checks.Add(theme + " Settings sections, search, and small-window render");
            }

            var registry = new ActionRegistry();
            var source = new SelectionOperationSource();
            var snapshot = new SelectionSnapshot("Hello العربية", TextAnalysis.PlainText, source.Begin(default), false, SelectionProviderKind.Manual);
            var palette = new ActionPalette(snapshot, registry);
            ((TextBox)palette.FindName("SearchBox")).Text = "upper";
            var list = (System.Windows.Controls.ListBox)palette.FindName("ActionsList");
            Require(list.Items.Count == 1, "Palette filtering failed");
            Require(((TextBlock)palette.FindName("PreviewText")).Text == "HELLO العربية", "Palette preview changed text");
            Require(!((ComboBoxItem)((System.Windows.Controls.ComboBox)palette.FindName("DestinationBox")).Items[1]).IsEnabled, "Read-only palette offered replacement");
            Render(palette, "palette", 580, 565); palette.Close();
            checks.Add("Palette filter, exact preview, read-only destination");

            var toolbar = new ToolbarWindow { Registry = registry };
            SettingsManager.Current.PinnedActionIds = registry.GetAllActionsForCategory(ActionCategory.Transform).Select(a => a.Id).ToList();
            // Build the production controls without showing a native toolbar or changing focus.
            SetField(toolbar, "_actionGroups", registry.GetActions("text", TextAnalysis.PlainText));
            foreach (double width in new[] { 600d, 400d })
            {
                ((Border)toolbar.FindName("MainBorder")).MaxWidth = width;
                toolbar.RebuildInlineActions();
                var main = (FrameworkElement)toolbar.FindName("MainToolbar");
                main.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Require(main.DesiredSize.Width <= width, $"Toolbar controls overflow at {width} DIPs: {main.DesiredSize.Width}");
                var more = (Button)toolbar.FindName("MoreButton");
                Require(more.Visibility == Visibility.Visible && more.Tag is List<IAction> { Count: > 0 }, "Pinned actions did not overflow into More");
                Require(System.Windows.Automation.AutomationProperties.GetName(more).Length > 0, "More has no accessible name");
                Render(toolbar, $"toolbar-{width}", width, 60);
            }
            toolbar.Close(); checks.Add("Toolbar width budget and accessible More at 400/600 DIPs");

            var popup = new ResultPopup();
            SetField(popup, "_fetch", (Func<CancellationToken, Task<LookupResult>>)(_ => throw new TaskCanceledException()));
            await popup.RunFetchAsync();
            Require(((TextBlock)popup.FindName("LoadingText")).Visibility == Visibility.Collapsed, "Timeout left Loading visible");
            Require(((Button)popup.FindName("RetryButton")).Visibility == Visibility.Visible, "Timeout has no retry");
            Require(((Button)popup.FindName("CopyButton")).Visibility == Visibility.Collapsed, "Timeout can be copied as a result");
            Render(popup, "lookup-timeout", 430, 250);
            var pending = new TaskCompletionSource<LookupResult>();
            SetField(popup, "_fetch", (Func<CancellationToken, Task<LookupResult>>)(_ => pending.Task));
            var oldRequest = popup.RunFetchAsync();
            SetField(popup, "_cts", new CancellationTokenSource());
            SetField(popup, "_fetch", (Func<CancellationToken, Task<LookupResult>>)(_ => Task.FromResult(LookupResult.Success("fresh"))));
            await popup.RunFetchAsync(); pending.SetResult(LookupResult.Success("stale")); await oldRequest;
            Require(((TextBlock)popup.FindName("ResultText")).Text == "fresh", "A stale request overwrote retry");
            popup.Close(); checks.Add("Rendered lookup timeout and stale retry suppression");

            var recipe = new TextRecipeEditor(new() { Name = "Clean", Steps = ["ws_trim", "case_upper"] });
            Render(recipe, "recipe-editor", 530, 600); recipe.Close();
            var qr = new QrCodeWindow("مرحبا ChatGPT 👋"); Render(qr, "local-qr", 400, 500); qr.Close();
            checks.Add("Recipe editor and local QR resources render");
        }
        catch (Exception ex) { failure = ex.ToString(); }
        File.WriteAllText(Path.Combine(RuntimePaths.DataDirectory, "self-test.json"), JsonSerializer.Serialize(new
        {
            passed = failure == null, checks, failure,
            limitations = "Compiled WPF layout checks do not establish physical keyboard/mouse, UIA provider, browser selection, or mixed-monitor behavior."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }

    private static void SetField(object instance, string name, object value) => instance.GetType()
        .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(instance, value);
    private static void Require(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
    private static void Render(Window window, string name, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        if (content is System.Windows.Controls.Panel panel) panel.Background = window.Background;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(RuntimePaths.DataDirectory, name + ".png")); png.Save(file);
    }
}
