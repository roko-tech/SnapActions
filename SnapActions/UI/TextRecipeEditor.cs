using System.Windows;
using System.Windows.Controls;
using SnapActions.Actions;
using SnapActions.Actions.UserActions;
using SnapActions.Config;
using SnapActions.Detection;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;

namespace SnapActions.UI;

internal sealed class TextRecipeEditor : Window
{
    internal TextRecipeDefinition Recipe { get; }
    private readonly IReadOnlyDictionary<string, IAction> _operations = new ActionRegistry().PureTextOperations();
    private readonly TextBox _name = new() { Padding = new Thickness(8) };
    private readonly TextBox _sample = new() { Text = "  Example text  ", AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _preview = new() { IsReadOnly = true, MinHeight = 80, TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _steps = new() { DisplayMemberPath = "Name", MinHeight = 100 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    internal TextRecipeEditor(TextRecipeDefinition? source)
    {
        Recipe = new() { Id = source?.Id ?? Guid.NewGuid().ToString("N"), Name = source?.Name ?? "", Steps = source?.Steps.ToList() ?? [] };
        Title = source == null ? "Create text recipe" : "Edit text recipe";
        Width = 570; Height = 650; MinWidth = 440; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BackgroundBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Recipe name", Margin = new Thickness(0, 0, 0, 6) });
        _name.Text = Recipe.Name; NameControl(_name, "Recipe name"); panel.Children.Add(_name);
        panel.Children.Add(new TextBlock { Text = "Steps run in this order (up to 12)", Margin = new Thickness(0, 14, 0, 6) });
        var choose = new ComboBox { ItemsSource = _operations.Values.OrderBy(a => a.Name).ToList(), DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 200 };
        NameControl(choose, "Text operation");
        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) }; controls.Children.Add(choose);
        controls.Children.Add(MakeButton("Add step", () =>
        {
            if (choose.SelectedItem is IAction action && Recipe.Steps.Count < 12) { Recipe.Steps.Add(action.Id); Refresh(); }
        }));
        controls.Children.Add(MakeButton("Remove", () => { int i = _steps.SelectedIndex; if (i >= 0) { Recipe.Steps.RemoveAt(i); Refresh(); } }));
        controls.Children.Add(MakeButton("Move up", () => Move(-1)));
        controls.Children.Add(MakeButton("Move down", () => Move(1)));
        panel.Children.Add(controls); NameControl(_steps, "Ordered recipe steps"); panel.Children.Add(_steps);
        panel.Children.Add(new TextBlock { Text = "Sample text", Margin = new Thickness(0, 14, 0, 6) });
        NameControl(_sample, "Recipe sample text"); _sample.MaxLength = Core.SelectionSnapshot.MaximumTextLength; panel.Children.Add(_sample);
        _sample.TextChanged += (_, _) => Preview();
        panel.Children.Add(new TextBlock { Text = "Preview", Margin = new Thickness(0, 10, 0, 6) });
        NameControl(_preview, "Recipe result preview"); panel.Children.Add(_preview); panel.Children.Add(_status);
        var footer = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(MakeButton("Cancel", () => { DialogResult = false; }));
        footer.Children.Add(MakeButton("Save recipe", () =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text) || Recipe.Steps.Count is < 1 or > 12)
            { _status.Text = "Enter a name and add at least one step."; return; }
            Recipe.Name = _name.Text.Trim(); DialogResult = true;
        }));
        _name.MaxLength = 80;
        panel.Children.Add(footer); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Refresh();
    }

    private void Move(int offset)
    {
        int i = _steps.SelectedIndex, next = i + offset;
        if (i < 0 || next < 0 || next >= Recipe.Steps.Count) return;
        (Recipe.Steps[i], Recipe.Steps[next]) = (Recipe.Steps[next], Recipe.Steps[i]); Refresh(); _steps.SelectedIndex = next;
    }
    private void Refresh()
    {
        _steps.ItemsSource = Recipe.Steps.Select(id => new { Name = _operations.TryGetValue(id, out var a) ? a.Name : "Unavailable: " + id }).ToList();
        _steps.SelectedIndex = Recipe.Steps.Count - 1; Preview();
    }
    private void Preview()
    {
        var result = new TextRecipeAction(Recipe, _operations).Execute(_sample.Text, new TextClassifier().Classify(_sample.Text));
        _preview.Text = result.Success ? result.ResultText : "";
        _preview.FlowDirection = ToolbarWindow.GetPreviewFlowDirection(_preview.Text ?? "");
        _status.Text = result.Success ? "Local preview — no clipboard changes" : result.Message;
    }
    private static Button MakeButton(string label, Action run)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(4, 3, 0, 3) };
        button.Click += (_, _) => run(); return button;
    }
    private static void NameControl(DependencyObject control, string name) => System.Windows.Automation.AutomationProperties.SetName(control, name);
}
