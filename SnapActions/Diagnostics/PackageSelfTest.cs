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
using DragEventArgs = System.Windows.DragEventArgs;

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
            CheckSettingsSaveFailures();
            checks.Add("Settings write/replace failures preserve saved pins, report errors, and recover on retry/reload");
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
            CheckToolbarCustomization(registry);
            checks.Add("Live settings refresh for browser/native providers, persistent read-only pins, drag/drop insertion, hide/show and unpin controls");
            await CheckToolbarPreviewAsync(registry);
            checks.Add("Hover preview reopen/leave lifecycle, constrained text layout, light/dark renders, and feedback after customization");

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
            SetField(popup, "_fetch", (Func<CancellationToken, Task<LookupResult>>)(_ => Task.FromResult(LookupResult.Success("fresh"))));
            ((Button)popup.FindName("RetryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pending.SetResult(LookupResult.Success("stale")); await oldRequest;
            Require(((TextBlock)popup.FindName("ResultText")).Text == "fresh", "A stale request overwrote retry");
            var closing = new TaskCompletionSource<LookupResult>();
            CancellationToken closingToken = default;
            SetField(popup, "_fetch", (Func<CancellationToken, Task<LookupResult>>)(ct => { closingToken = ct; return closing.Task; }));
            var closingRequest = popup.RunFetchAsync();
            popup.Close();
            Require(closingToken.IsCancellationRequested, "Closing the popup did not cancel the lookup");
            closing.SetResult(LookupResult.Success("after close")); await closingRequest;
            Require(((TextBlock)popup.FindName("ResultText")).Text == "fresh", "A request rendered after its popup closed");
            checks.Add("Rendered lookup timeout, actual Retry supersession, close cancellation and stale result suppression");

            var translation = new TranslationPopup();
            var translationHandle = new System.Windows.Interop.WindowInteropHelper(translation).EnsureHandle();
            Require(System.Windows.Interop.HwndSource.FromHwnd(translationHandle).CompositionTarget.RenderMode == System.Windows.Interop.RenderMode.SoftwareOnly,
                "Translation native frame did not use the compatible render mode");
            typeof(TranslationPopup).GetMethod("ShowFailure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(translation, ["Google Translate couldn't open. Check your connection and try again."]);
            Require(((Button)translation.FindName("RetryButton")).Visibility == Visibility.Visible, "Translation failure has no retry");
            Require(((StackPanel)translation.FindName("StatusPanel")).Visibility == Visibility.Visible, "Translation failure message is hidden");
            Require(((Grid)translation.FindName("BrowserHost")).Visibility == Visibility.Collapsed, "Translation failure left browser visible");
            Render(translation, "translation-unavailable", translation.Width, translation.Height);
            translation.Close();
            Require((bool)typeof(TranslationPopup).GetField("_closed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(translation)!, "Translation close did not dispose its lifetime");
            checks.Add("Native translation failure and Retry render, close lifetime disposal without network or WebView2 initialization");

            var recipe = new TextRecipeEditor(new() { Name = "Clean", Steps = ["ws_trim", "case_upper"] });
            Render(recipe, "recipe-editor", 530, 600); recipe.Close();
            checks.Add("Recipe editor resources render");
        }
        catch (Exception ex) { failure = ex.ToString(); }
        File.WriteAllText(Path.Combine(RuntimePaths.DataDirectory, "self-test.json"), JsonSerializer.Serialize(new
        {
            passed = failure == null, checks, failure,
            limitations = "Compiled WPF layout checks do not establish physical keyboard/mouse, UIA provider, browser selection, or mixed-monitor behavior."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }

    private static void CheckSettingsSaveFailures()
    {
        string path = Path.Combine(RuntimePaths.DataDirectory, "settings.json");
        string temp = path + ".tmp";
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var registry = new ActionRegistry();
        var delete = registry.GetAllActionsForCategory(ActionCategory.Transform).Single(a => a.Id == "delete_text");
        var toolbar = new ToolbarWindow { Registry = registry };
        SetField(toolbar, "_selectedText", "saved preferences");
        try
        {
            foreach (bool failTempWrite in new[] { true, false })
            {
                SettingsManager.Current.PinnedActionIds = ["case_upper", "delete_text", "paste_plain"];
                SettingsManager.Current.DisabledActionIds = ["paste_plain"];
                Require(SettingsManager.Save(), "Cannot establish saved preference baseline");
                byte[] saved = File.ReadAllBytes(path);
                ToolbarPreferences.Pin(SettingsManager.Current, delete, "case_upper");
                ToolbarPreferences.SetHidden(SettingsManager.Current, delete, true);
                FileStream? locked = null;
                try
                {
                    if (failTempWrite) Directory.CreateDirectory(temp);
                    else locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    Require(!SettingsManager.Save(), "A blocked settings write reported success");
                    Require(SettingsManager.LastSaveError != null, "Failed settings save has no error");
                    Require(((TextBlock)toolbar.FindName("CustomizationHint")).Text == SettingsManager.LastSaveError,
                        "Toolbar did not show the settings save error");
                    Require(File.ReadAllBytes(path).SequenceEqual(saved), "Failed save changed the last saved settings");
                    Require(SettingsManager.Current.PinnedActionIds.SequenceEqual(new[] { "delete_text", "case_upper", "paste_plain" }),
                        "Failed save lost the pending pin order");
                    Require(SettingsManager.Current.DisabledActionIds.SequenceEqual(new[] { "paste_plain", "delete_text" }),
                        "Failed save lost the pending hidden actions");
                }
                finally
                {
                    locked?.Dispose();
                    if (failTempWrite) Directory.Delete(temp);
                }
                // A reload after a failed save must recover the complete last successful version.
                SettingsManager.Load();
                Require(SettingsManager.Current.PinnedActionIds.SequenceEqual(new[] { "case_upper", "delete_text", "paste_plain" })
                    && SettingsManager.Current.DisabledActionIds.SequenceEqual(new[] { "paste_plain" }),
                    "Reload after failure lost saved toolbar preferences");
                ToolbarPreferences.Pin(SettingsManager.Current, delete, "case_upper");
                ToolbarPreferences.SetHidden(SettingsManager.Current, delete, true);
                Require(SettingsManager.Save() && SettingsManager.LastSaveError == null, "Settings did not recover after the lock was removed");
                Require(!File.Exists(temp), "Successful settings retry left a temporary file");
                Require(((TextBlock)toolbar.FindName("CustomizationHint")).Text.Contains("Drag onto"), "Successful retry left a stale toolbar error");
                SettingsManager.Load();
                Require(SettingsManager.Current.PinnedActionIds.SequenceEqual(new[] { "delete_text", "case_upper", "paste_plain" })
                    && SettingsManager.Current.DisabledActionIds.SequenceEqual(new[] { "paste_plain", "delete_text" }),
                    "Retried toolbar preferences did not survive reload");
            }
        }
        finally
        {
            toolbar.Close();
            if (original == null) File.Delete(path); else File.WriteAllBytes(path, original);
            SettingsManager.Load();
        }
    }

    private static void CheckToolbarCustomization(ActionRegistry registry)
    {
        var settings = SettingsManager.Current;
        settings.UserActions = Enumerable.Range(0, 8).Select(i => new UserAction
        { Id = $"toolbar_test_{i}", Name = $"Suggestion {i + 1}", UrlTemplate = "https://example.com/?q={0}" }).ToList();
        settings.PinnedActionIds = ["search_twitter", "search_google", "delete_text", "paste_plain"];
        settings.ShowEncodeActions = false;
        var toolbar = new ToolbarWindow { Registry = registry };
        SetField(toolbar, "_selectedText", "one two");
        SetField(toolbar, "_appName", "brave");
        var border = (Border)toolbar.FindName("MainBorder");
        border.MaxWidth = 1200;
        var pins = (StackPanel)toolbar.FindName("PinnedActionsPanel");
        var context = (StackPanel)toolbar.FindName("ContextActionsPanel");
        Button Pin(string id) => pins.Children.OfType<Button>().Single(b => ((IAction)b.Tag).Id == id);
        foreach (var provider in new[] { SelectionProviderKind.Browser, SelectionProviderKind.UiAutomation })
        foreach (bool editable in new[] { true, false })
        {
            SetField(toolbar, "_selectionProvider", provider);
            SetField(toolbar, "_isEditable", editable);
            foreach (int limit in new[] { 4, 8 })
            {
                settings.MaxInlineContextActions = limit;
                Require(SettingsManager.Save(), "Cannot persist toolbar preferences");
                Require(context.Children.Count == limit, $"{provider} kept a stale inline limit: {context.Children.Count} != {limit}");
                Require(pins.Children.Count == 4, "Pinned actions disappeared because of selection capability or the context limit");
                var paste = (IAction)Pin("paste_plain").Tag;
                Require(Pin("delete_text").IsEnabled == editable && Pin("paste_plain").IsEnabled == (editable && paste.CanExecute("one two", TextAnalysis.PlainText)), "Read-only pins have incorrect execution capability");
            }
        }
        Require(Pin("paste_plain").ToolTip.ToString()!.Contains("editable"), "Read-only pin has no explanation");
        Require(Pin("paste_plain").Content is System.Windows.Shapes.Path && Pin("paste_plain").Width == 36,
            "Pinned Paste must use the compact clipboard icon");
        Require(System.Windows.Automation.AutomationProperties.GetName(Pin("paste_plain")) == "Paste Plain Text", "Icon-only Paste lost its accessible name");
        Render(toolbar, "toolbar-eight-suggestions-readonly", 1200, 70);
        settings.ShowTransformActions = false; toolbar.RefreshActions();
        Require(pins.Children.Count == 4, "Hiding a category menu also hid its pins");
        settings.AppHiddenActions["BRAVE"] = ["delete_text"]; toolbar.RefreshActions();
        Require(!pins.Children.OfType<Button>().Any(b => ((IAction)b.Tag).Id == "delete_text"), "Pin bypassed the current app's hidden actions");
        SetField(toolbar, "_appName", "notepad"); toolbar.RefreshActions();
        Require(pins.Children.Count == 4, "Browser profile leaked into a native app");
        SetField(toolbar, "_appName", "brave"); settings.AppHiddenActions.Clear();
        settings.ShowTransformActions = true; toolbar.RefreshActions();

        // Exercise real WPF drop routes. The internal event constructor is needed because
        // no system mouse input or OLE drag loop is started by the package self-test.
        var dropConstructor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        void Drop(IAction action, string? targetId = null, bool after = false)
        {
            Render(toolbar, "toolbar-drag", 1200, 70);
            var target = targetId == null ? null : Pin(targetId);
            var point = target == null ? new Point(border.ActualWidth - 6, 20)
                : target.TranslatePoint(new Point(target.ActualWidth * (after ? 0.9 : 0.1), 18), border);
            SetField(toolbar, "_draggingAction", action);
            foreach (var routedEvent in new[] { DragDrop.PreviewDragOverEvent, DragDrop.PreviewDropEvent })
            {
                var args = (DragEventArgs)dropConstructor.Invoke([new DataObject("SnapActions.ToolbarAction", action.Id), DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, border, point]);
                args.RoutedEvent = routedEvent;
                border.RaiseEvent(args);
                Require(args.Effects == DragDropEffects.Move, "Toolbar rejected its own action drag");
                if (routedEvent == DragDrop.PreviewDragOverEvent)
                    Require(((Border)toolbar.FindName("PinDropIndicator")).Visibility == Visibility.Visible, "Drop insertion point is invisible");
            }
            SetField(toolbar, "_draggingAction", null);
            toolbar.GetType().GetMethod("FinishCustomizationInteraction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(toolbar, null);
        }
        var upper = registry.GetAllActionsForCategory(ActionCategory.Transform).Single(a => a.Id == "case_upper");
        Drop(upper, "delete_text");
        Require(settings.PinnedActionIds.SequenceEqual(["search_twitter", "search_google", "case_upper", "delete_text", "paste_plain"]), "Drop did not pin at the chosen position");
        Drop(upper, "paste_plain", true);
        Require(settings.PinnedActionIds.Last() == upper.Id, "Right-half drop did not move after the target");
        Drop(upper, "search_twitter");
        Require(settings.PinnedActionIds.First() == upper.Id, "Left-half drop did not move before the target");

        var hide = Pin(upper.Id).ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(i => (string)i.Header == "Hide action");
        hide.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Require(!pins.Children.OfType<Button>().Any(b => ((IAction)b.Tag).Id == upper.Id), "Hide left a stale pinned button");
        Require(settings.PinnedActionIds.Contains(upper.Id), "Hiding lost the pin's order");
        ((Button)toolbar.FindName("CustomizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var catalog = (WrapPanel)toolbar.FindName("SubMenuPanel");
        var catalogPopup = (System.Windows.Controls.Primitives.Popup)toolbar.FindName("SubMenuPopup");
        var catalogContent = (FrameworkElement)catalogPopup.Child;
        catalogContent.Measure(new Size(420, 400)); catalogContent.Arrange(new Rect(0, 0, 420, 400)); catalogContent.UpdateLayout();
        var catalogBitmap = new RenderTargetBitmap(420, 400, 96, 96, PixelFormats.Pbgra32);
        catalogBitmap.Render(catalogContent);
        var catalogPng = new PngBitmapEncoder(); catalogPng.Frames.Add(BitmapFrame.Create(catalogBitmap));
        using (var image = File.Create(Path.Combine(RuntimePaths.DataDirectory, "toolbar-customization.png"))) catalogPng.Save(image);
        var restore = catalog.Children.OfType<Button>().Single(b => ((IAction)b.Tag).Id == upper.Id);
        restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(pins.Children.OfType<Button>().Any(b => ((IAction)b.Tag).Id == upper.Id), "Catalog did not restore a hidden pin");
        var unpin = Pin(upper.Id).ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(i => (string)i.Header == "Unpin from toolbar");
        unpin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Require(!settings.PinnedActionIds.Contains(upper.Id), "Unpin failed");
        Require(catalog.Children.OfType<Button>().Any(b => ((IAction)b.Tag).Id == upper.Id), "Unpin hid the action from its menu");
        settings.PinnedActionIds.Clear(); toolbar.RefreshActions(); Drop(upper);
        Require(settings.PinnedActionIds.SequenceEqual([upper.Id]), "Cannot pin the first action onto an empty toolbar");
        toolbar.Close();
    }

    private static async Task CheckToolbarPreviewAsync(ActionRegistry registry)
    {
        SettingsManager.Current.PinnedActionIds = ["case_upper"];
        var toolbar = new ToolbarWindow { Registry = registry };
        try
        {
            SetField(toolbar, "_selectedText", "Hello العربية");
            toolbar.RefreshActions();
            var menu = (Button)toolbar.FindName("TransformButton");
            var popup = (System.Windows.Controls.Primitives.Popup)toolbar.FindName("SubMenuPopup");
            var preview = (Border)toolbar.FindName("PreviewBorder");
            var text = (TextBlock)toolbar.FindName("PreviewText");
            // WPF defers IsOpen on an unloaded popup. Load a transparent, non-activating
            // host so category toggles exercise the real popup lifecycle without taking focus.
            ((FrameworkElement)popup.Child).Opacity = 0;
            ((FrameworkElement)popup.Child).IsHitTestVisible = false;
            toolbar.Left = toolbar.Top = -32000;
            toolbar.Topmost = false;
            toolbar.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            void Hover(Button button, RoutedEvent route) => button.RaiseEvent(new System.Windows.Input.MouseEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = route });

            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!popup.IsOpen, "Category toggle did not close the menu");
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var upper = ((WrapPanel)toolbar.FindName("SubMenuPanel")).Children.OfType<Button>().Single(b => ((IAction)b.Tag).Id == "case_upper");
            Hover(upper, UIElement.MouseEnterEvent);
            Require(popup.IsOpen && preview.Visibility == Visibility.Visible && text.Opacity == 1,
                $"Hover preview stayed hidden after closing and reopening a category: open={popup.IsOpen}, band={preview.Visibility}, opacity={text.Opacity}, enabled={upper.IsEnabled}, text={text.Text}");
            Require(new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text == "HELLO العربية", "Hover preview changed the result text");
            Hover(upper, UIElement.MouseLeaveEvent);
            Require(popup.IsOpen, "Leaving a submenu action closed the action menu");
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pin = ((StackPanel)toolbar.FindName("PinnedActionsPanel")).Children.OfType<Button>().Single();
            Hover(pin, UIElement.MouseEnterEvent);
            Require(popup.IsOpen && preview.Visibility == Visibility.Visible && text.Opacity == 1, "Inline hover did not restore the preview");
            Require(((FrameworkElement)toolbar.FindName("SubMenuHeader")).Visibility == Visibility.Collapsed, "Hover-only preview has an empty menu heading");
            Hover(pin, UIElement.MouseLeaveEvent);
            Require(!popup.IsOpen, "Inline hover left an empty popup behind");

            foreach (string theme in new[] { "dark", "light" })
            {
                SettingsManager.Current.Theme = theme; ThemeManager.Apply();
                SetField(toolbar, "_selectedText", string.Concat(Enumerable.Repeat("Hello العربية ", 15)));
                Hover(pin, UIElement.MouseEnterEvent);
                RenderPreview("toolbar-hover-" + theme);
                Require(text.ActualWidth <= preview.ActualWidth - preview.Padding.Left - preview.Padding.Right,
                    "Long preview text escaped its band instead of trimming");
                Hover(pin, UIElement.MouseLeaveEvent);
            }

            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(((FrameworkElement)toolbar.FindName("SubMenuHeader")).Visibility == Visibility.Visible, "Opening a menu after hovering lost its heading");
            Hover(pin, UIElement.MouseEnterEvent);
            Hover(pin, UIElement.MouseLeaveEvent);
            Require(popup.IsOpen, "Leaving an inline action closed a real submenu");
            ((Button)toolbar.FindName("GearButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(preview.Visibility == Visibility.Collapsed, "Customization left an empty preview band");
            popup.IsOpen = false;
            var toast = (Task)toolbar.GetType().GetMethod("ShowCopiedToast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(toolbar, null)!;
            Require(popup.IsOpen && preview.Visibility == Visibility.Visible && text.Opacity == 1, "Copy feedback stayed hidden after customization");
            await toast;

            void RenderPreview(string name)
            {
                // Capture the popup after closing its transparent native surface.
                popup.IsOpen = false;
                var content = (FrameworkElement)popup.Child;
                content.Opacity = 1;
                content.Measure(new Size(420, 400));
                content.Arrange(new Rect(new Point(), content.DesiredSize)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var image = File.Create(Path.Combine(RuntimePaths.DataDirectory, name + ".png"))) png.Save(image);
                content.Opacity = 0;
            }
        }
        finally { ((System.Windows.Controls.Primitives.Popup)toolbar.FindName("SubMenuPopup")).IsOpen = false; toolbar.Close(); }
    }

    private static void SetField(object instance, string name, object? value) => instance.GetType()
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
