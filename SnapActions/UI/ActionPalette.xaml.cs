using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapActions.Actions;
using SnapActions.Core;
using SnapActions.Detection;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SnapActions.UI;

public partial class ActionPalette : Window
{
    private readonly SelectionSnapshot? _selection;
    private readonly ActionRegistry _registry;
    private readonly SelectionOperationSource _manualOperations = new();
    private readonly string? _appName;
    private List<IAction> _actions = [];
    private bool _ready, _running;

    internal ActionPalette(SelectionSnapshot? selection, ActionRegistry registry)
    {
        _selection = selection;
        _registry = registry;
        _appName = ForegroundApp.GetActiveProcessName();
        InitializeComponent();
        SourceBox.Text = selection?.Text ?? "";
        SourceBox.IsReadOnly = selection != null;
        SourceBox.FlowDirection = ToolbarWindow.GetPreviewFlowDirection(SourceBox.Text);
        SourceLabel.Text = selection == null ? "No selection available — enter text here" : "Selected text";
        ((ComboBoxItem)DestinationBox.Items[1]).IsEnabled = selection?.CanReplace == true;
        DestinationBox.SelectedIndex = selection?.CanReplace == true && Config.SettingsManager.Current.ReplaceSelectionOnTransform ? 1 : 0;
        _ready = true;
        RebuildActions();
        Loaded += (_, _) => { if (_selection == null) SourceBox.Focus(); else SearchBox.Focus(); };
        Deactivated += (_, _) => { if (!_running) Close(); };
        Closed += (_, _) => { _selection?.Operation.InvalidateIfCurrent(); _manualOperations.Invalidate(); };
    }

    private void RebuildActions()
    {
        string text = _selection?.Text ?? SourceBox.Text;
        var analysis = _selection?.Analysis ?? new TextClassifier().Classify(text);
        _actions = _registry.GetActions(text, analysis, _appName).SelectMany(g => g.Actions)
            .Where(a => a is not IOperationAction || _selection?.CanReplace == true).ToList();
        FilterActions();
    }

    private void FilterActions()
    {
        if (!_ready) return;
        var terms = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ActionsList.ItemsSource = _actions.Where(a => terms.All(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
            || a.Category.ToString().Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        ActionsList.SelectedIndex = ActionsList.Items.Count > 0 ? 0 : -1;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => FilterActions();
    private void Source_Changed(object sender, TextChangedEventArgs e) { if (_ready && _selection == null) RebuildActions(); }
    private void Destination_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdatePreview(); }
    private void Action_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdatePreview(); }

    private void UpdatePreview()
    {
        if (ActionsList.SelectedItem is not IAction action) { RunButton.IsEnabled = false; PreviewText.Text = "No matching actions"; return; }
        RunButton.IsEnabled = true;
        if (action.IsPreviewSafe)
        {
            string text = _selection?.Text ?? SourceBox.Text;
            ActionResult result;
            try { result = action.Execute(text, _selection?.Analysis ?? new TextClassifier().Classify(text)); }
            catch { result = new(false, Message: "This selection could not be previewed."); }
            PreviewText.Text = result.Success ? result.ResultText : result.Message;
            PreviewText.FlowDirection = ToolbarWindow.GetPreviewFlowDirection(PreviewText.Text ?? "");
            RunButton.IsEnabled = result.Success;
            RunButton.Content = DestinationBox.SelectedIndex == 1 ? "Replace selection" : "Copy result";
            DestinationBox.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewText.Text = action is IOperationAction ? "This action changes text in the original app." : $"Run {action.Name}";
            RunButton.Content = action.Name;
            DestinationBox.Visibility = Visibility.Collapsed;
        }
    }

    private void Palette_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key is Key.Down or Key.Up && !SourceBox.IsKeyboardFocusWithin)
        {
            ActionsList.SelectedIndex = Math.Clamp(ActionsList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, ActionsList.Items.Count - 1));
            ActionsList.ScrollIntoView(ActionsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !SourceBox.IsKeyboardFocusWithin) { e.Handled = true; Run_Click(this, e); }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_running || !RunButton.IsEnabled || ActionsList.SelectedItem is not IAction action) return;
        _running = true;
        var selection = _selection ?? new SelectionSnapshot(SourceBox.Text, new TextClassifier().Classify(SourceBox.Text),
            _manualOperations.Begin(default), false, SelectionProviderKind.Manual);
        Hide();
        try
        {
            if (_selection != null && !await GlobalHotkey.ReturnToTargetAsync(selection.Operation))
            {
                ResultPopup.ShowLocalResult("Action unavailable", "The original window could not be focused. Select the text again.");
                Close(); return;
            }
            var result = await ActionRunner.ExecuteAsync(action, selection);
            if (result.Success && result.ResultText != null)
                result = await ActionRunner.ApplyTextAsync(result.ResultText, selection,
                    DestinationBox.SelectedIndex == 1 ? ResultDestination.Replace : ResultDestination.Copy);
            if (!result.Success) ResultPopup.ShowLocalResult("Action unavailable", result.Message ?? "The action could not be completed");
            Close();
        }
        catch (Exception ex)
        {
            Helpers.Log.Warn($"Palette action failed ({ex.GetType().Name})");
            Close();
        }
    }
}
