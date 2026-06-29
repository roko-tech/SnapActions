using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            preview = $"Search {action.Name} for: \"{Truncate(_selectedText, 50)}\"";
        else
            preview = action.Name;

        // For color selections, also show a swatch for *non*-pure actions like Preview Color.
        if (_analysis.Type == TextType.ColorCode && swatchHex == null)
            swatchHex = _selectedText;

        PreviewText.Text = preview;
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

        // Open popup in hover-preview mode: empty submenu panel, empty title row.
        _hoverPreviewMode = true;
        SubMenuPanel.Children.Clear();
        SubMenuTitle.Text = "";
        UpdatePreviewBand(action);
        SubMenuPopup.IsOpen = true;
        StartDismissTimer();
    }

    private void InlineButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        ResetPreview();

    private void ResetPreview()
    {
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
        if (!SubMenuPopup.IsOpen)
        {
            // The user clicked an inline button (no submenu open). Open the submenu briefly so
            // the preview band — which lives inside it — is visible.
            SubMenuPanel.Children.Clear();
            SubMenuTitle.Text = "";
            SubMenuPopup.IsOpen = true;
        }
        SetSwatch(null);
        PreviewText.Text = "Copied to clipboard";
        PreviewText.Opacity = 1;
        await Task.Delay(450);
    }

    private async Task ShowFailureAndHide(string message)
    {
        int gen = _generation;
        // Make sure the popup is open so PreviewText is visible.
        if (!SubMenuPopup.IsOpen)
        {
            SubMenuPanel.Children.Clear();
            SubMenuTitle.Text = "Error";
            SubMenuPopup.IsOpen = true;
        }
        PreviewText.Text = message;
        PreviewText.Opacity = 1;
        // Short visible window — long enough to read, short enough not to feel sticky.
        await Task.Delay(1500);
        // Don't hide if a new selection reshowed the toolbar during the delay.
        if (_generation == gen) HideToolbar();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
