using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text;
using SnapActions.Actions;
using SnapActions.Detection;

namespace SnapActions.UI;

// Hover-preview band (the strip beneath the sub-menu popup that shows a live preview of what
// a pure action would produce, plus a color swatch for color selections). Also hosts the small
// "Copied!" toast and the failure-message UI — both reuse the same band so we don't carry a
// second widget for them.
public partial class ToolbarWindow
{
    // Hover-preview executes synchronously on the UI thread. Keep this small — JSON/XML parse
    // on a multi-KB blob from a MouseEnter event is wasted work; the truncated preview gets cut
    // to 120 chars in UpdatePreviewBand anyway. Real Execute on click can do the heavy parse.
    private const int MaxPreviewExecuteChars = 4 * 1024;

    private void SubMenuButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggingAction != null) return;
        if (sender is not Button { Tag: IAction action }) return;
        UpdatePreviewBand(action);
    }

    private void SubMenuButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        ResetPreview();

    /// <summary>
    /// Computes the preview text + optional color swatch for an action and writes them into the
    /// preview band. Doesn't open or close the popup — caller is responsible for that.
    /// </summary>
    private void UpdatePreviewBand(IAction action)
    {
        string preview;
        string? label = null;
        string? swatchHex = null;

        // Preview is opt-in via IAction.IsPreviewSafe — only pure actions run on hover.
        if (action.IsPreviewSafe
            && !string.IsNullOrEmpty(_selectedText)
            && _selectedText.Length <= MaxPreviewExecuteChars)
        {
            try
            {
                var r = action.Execute(_selectedText, _analysis);
                // Prefer ResultText (the "what gets copied" output). Fall back to Message so
                // actions that don't produce clipboard text — PreviewColor returns
                // Message="Color: #89B4FA" with null ResultText — still surface useful preview.
                preview = r.ResultText != null
                    ? Truncate(r.ResultText, 120)
                    : (r.Message != null ? Truncate(r.Message, 120) : action.Name);
                if (_analysis.Type == TextType.ColorCode)
                    swatchHex = r.ResultText ?? _selectedText;
            }
            catch { preview = action.Name; }
        }
        else if (action.Category == ActionCategory.Search)
        {
            label = $"Search {action.Name} for: ";
            preview = Truncate(_selectedText, 50);
        }
        else
            preview = action.Name;

        // For color selections, also show a swatch for *non*-pure actions like Preview Color.
        if (_analysis.Type == TextType.ColorCode && swatchHex == null)
            swatchHex = _selectedText;

        SetPreviewContent(PreviewText, preview, label,
            label != null || action.Category == ActionCategory.Transform ? _selectionFlowDirection : null);
        PreviewBorder.Visibility = Visibility.Visible;
        PreviewText.Opacity = 1;
        SetSwatch(swatchHex);
    }

    /// <summary>
    /// Hover handler for the *inline* main-toolbar action buttons (CreateActionButton /
    /// CreatePinnedButton). Opens the sub-menu popup with just the preview band visible — the
    /// preview band lives inside the popup, so without opening it the user sees nothing.
    /// </summary>
    private void InlineButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggingAction != null) return;
        if (sender is not Button { Tag: IAction action }) return;
        // Standard tooltips already cover the action name for non-previewable actions; only
        // bother opening the popup when there's something interesting to show.
        if (!action.IsPreviewSafe && action.Category != ActionCategory.Search) return;
        if (string.IsNullOrEmpty(_selectedText)) return;

        // Don't override an already-open submenu — the user is interacting with that. Just refresh
        // its preview band with the inline action's preview.
        if (SubMenuPopup.IsOpen && !_hoverPreviewMode)
        {
            UpdatePreviewBand(action);
            return;
        }

        // Open popup in hover-preview mode: empty submenu panel, empty title row, no gear.
        _hoverPreviewMode = true;
        SubMenuPanel.Children.Clear();
        SubMenuTitle.Text = "";
        SubMenuHeader.Visibility = Visibility.Collapsed;
        GearButton.Visibility = Visibility.Collapsed;
        CustomizationHint.Visibility = Visibility.Collapsed;
        UpdatePreviewBand(action);
        SubMenuPopup.IsOpen = true;
        StartDismissTimer();
    }

    private void InlineButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ResetPreview();
        if (_hoverPreviewMode)
        {
            SubMenuPopup.IsOpen = false;
            _hoverPreviewMode = false;
        }
    }

    private void ResetPreview()
    {
        // Reserve space while browsing a menu so hovering doesn't move its actions.
        PreviewBorder.Visibility = _editMode ? Visibility.Collapsed : Visibility.Hidden;
        PreviewText.Opacity = 0;
        SetSwatch(null);
    }

    private void SetSwatch(string? colorText)
    {
        if (string.IsNullOrEmpty(colorText))
        {
            ColorSwatch.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var converter = new System.Windows.Media.BrushConverter();
            var brush = converter.ConvertFromString(colorText.Trim()) as Brush;
            if (brush == null) { ColorSwatch.Visibility = Visibility.Collapsed; return; }
            ColorSwatch.Background = brush;
            ColorSwatch.Visibility = Visibility.Visible;
        }
        catch
        {
            ColorSwatch.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Briefly flash a "Copied!" indicator in the preview band before the toolbar fades out.
    /// Uses the existing PreviewText/PreviewBorder so we don't need another widget.
    /// </summary>
    private async Task ShowCopiedToast()
    {
        _hoverPreviewMode = false;
        if (!SubMenuPopup.IsOpen)
        {
            // The user clicked an inline button (no submenu open). Open the submenu briefly so
            // the preview band — which lives inside it — is visible.
            SubMenuPanel.Children.Clear();
            SubMenuTitle.Text = "";
            SubMenuHeader.Visibility = Visibility.Collapsed;
            GearButton.Visibility = Visibility.Collapsed;
            CustomizationHint.Visibility = Visibility.Collapsed;
            SubMenuPopup.IsOpen = true;
        }
        SetSwatch(null);
        SetPreviewContent(PreviewText, "Copied to clipboard");
        PreviewBorder.Visibility = Visibility.Visible;
        PreviewText.Opacity = 1;
        await Task.Delay(450);
    }

    private async Task ShowFailureAndHide(string message)
    {
        int gen = _generation;
        _hoverPreviewMode = false;
        // Make sure the popup is open so PreviewText is visible.
        if (!SubMenuPopup.IsOpen)
        {
            SubMenuPanel.Children.Clear();
            SubMenuTitle.Text = "Error";
            SubMenuHeader.Visibility = Visibility.Visible;
            GearButton.Visibility = Visibility.Collapsed;
            CustomizationHint.Visibility = Visibility.Collapsed;
            SubMenuPopup.IsOpen = true;
        }
        SetSwatch(null);
        SetPreviewContent(PreviewText, message);
        PreviewBorder.Visibility = Visibility.Visible;
        PreviewText.Opacity = 1;
        // Short visible window — long enough to read, short enough not to feel sticky.
        await Task.Delay(1500);
        // Don't hide if a new selection reshowed the toolbar during the delay.
        if (_generation == gen) HideToolbar();
    }

    // A Span gives the selected phrase its own bidi scope; the English search label must not
    // determine its reading direction. This changes WPF presentation only, never _selectedText.
    internal static void SetPreviewContent(TextBlock target, string text, string? label = null,
        FlowDirection? direction = null)
    {
        var textDirection = direction ?? GetPreviewFlowDirection(text);
        target.Inlines.Clear();
        target.FlowDirection = label == null ? textDirection : FlowDirection.LeftToRight;
        if (label == null)
            target.Inlines.Add(new Run(text));
        else
        {
            target.Inlines.Add(new Run(label));
            target.Inlines.Add(new Span(new Run($"\"{text}\"")) { FlowDirection = textDirection });
        }
    }

    internal static FlowDirection GetPreviewFlowDirection(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            if (value is 0x200F or 0x061C) return FlowDirection.RightToLeft;
            if (value == 0x200E) return FlowDirection.LeftToRight;
            if (!Rune.IsLetter(rune)) continue;
            return value is >= 0x0590 and <= 0x08FF
                or >= 0xFB1D and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF
                or >= 0x1EE00 and <= 0x1EEFF
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }
        return FlowDirection.LeftToRight;
    }

    // Internal for tests. Backs up one UTF-16 unit when the cut would split a surrogate pair,
    // so an emoji straddling the limit doesn't render as a lone-surrogate "�".
    internal static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        if (max > 0 && char.IsHighSurrogate(s[max - 1])) max--;
        return s[..max] + "...";
    }
}
