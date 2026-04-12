using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnapActions.Config;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SnapActions.UI;

public partial class SettingsWindow : Window
{
    private bool _loading = true;
    private Brush? _textBrush;
    private Brush? _secondaryBrush;

    public SettingsWindow()
    {
        InitializeComponent();
        _textBrush = (Brush)FindResource("TextBrush");
        _secondaryBrush = (Brush)FindResource("TextSecondaryBrush");
        LoadSettings();
        _loading = false;
    }

    private void LoadSettings()
    {
        var s = SettingsManager.Current;
        EnabledCheck.IsChecked = s.Enabled;
        AutoStartCheck.IsChecked = s.AutoStart;
        ReplaceSelectionCheck.IsChecked = s.ReplaceSelectionOnTransform;

        SelectComboByTag(DismissTimeCombo, s.ToolbarDismissTimeout.ToString(), 2);
        SelectComboByTag(ShowDelayCombo, s.ToolbarShowDelay.ToString(), 0);
        SelectComboByTag(LanguageCombo, s.SearchLanguage, 0);

        ShowTransformCheck.IsChecked = s.ShowTransformActions;
        ShowEncodeCheck.IsChecked = s.ShowEncodeActions;
        ShowSearchCheck.IsChecked = s.ShowSearchActions;

        BuildSearchEnginesList();
        ExcludedAppsBox.Text = string.Join("\n", s.ExcludedApps);
    }

    private static void SelectComboByTag(ComboBox combo, string tag, int fallback)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == tag)
            { combo.SelectedIndex = i; return; }
        combo.SelectedIndex = fallback;
    }

    private void BuildSearchEnginesList()
    {
        SearchEnginesPanel.Children.Clear();
        foreach (var engine in SettingsManager.Current.SearchEngines)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            var cb = new CheckBox
            {
                Content = engine.Name, IsChecked = engine.Enabled,
                Foreground = _textBrush, Width = 150, Tag = engine.Id
            };
            cb.Checked += EngineToggle_Changed;
            cb.Unchecked += EngineToggle_Changed;
            row.Children.Add(cb);

            if (!engine.IsBuiltIn)
            {
                row.Children.Add(new TextBlock
                {
                    Text = engine.UrlTemplate.Length > 40 ? engine.UrlTemplate[..40] + "..." : engine.UrlTemplate,
                    FontSize = 10, Foreground = _secondaryBrush,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0)
                });

                var delBtn = new System.Windows.Controls.Button
                {
                    Content = "X", Width = 22, Height = 22, FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent, Foreground = _secondaryBrush,
                    BorderThickness = new Thickness(0), Tag = engine.Id,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                delBtn.Click += DeleteEngine_Click;
                row.Children.Add(delBtn);
            }

            SearchEnginesPanel.Children.Add(row);
        }
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = SettingsManager.Current;
        s.Enabled = EnabledCheck.IsChecked == true;
        s.ReplaceSelectionOnTransform = ReplaceSelectionCheck.IsChecked == true;
        s.ShowTransformActions = ShowTransformCheck.IsChecked == true;
        s.ShowEncodeActions = ShowEncodeCheck.IsChecked == true;
        s.ShowSearchActions = ShowSearchCheck.IsChecked == true;
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enable = AutoStartCheck.IsChecked == true;
        Task.Run(() => SettingsManager.SetAutoStart(enable));
    }

    private void ShowDelay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ShowDelayCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
            SettingsManager.Current.ToolbarShowDelay = ms;
    }

    private void DismissTime_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (DismissTimeCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
            SettingsManager.Current.ToolbarDismissTimeout = ms;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item)
            SettingsManager.Current.SearchLanguage = item.Tag?.ToString() ?? "";
    }

    private void EngineToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string id }) return;
        var engine = SettingsManager.Current.SearchEngines.FirstOrDefault(en => en.Id == id);
        if (engine != null) engine.Enabled = ((CheckBox)sender).IsChecked == true;
    }

    private void DeleteEngine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        SettingsManager.Current.SearchEngines.RemoveAll(en => en.Id == id);
        BuildSearchEnginesList();
    }

    private void AddCustomEngine_Click(object sender, RoutedEventArgs e)
    {
        var name = CustomNameBox.Text.Trim();
        var url = CustomUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

        if (!url.Contains("{0}"))
            url += (url.Contains('?') ? "&" : "?") + "q={0}";

        var id = "custom_" + name.ToLowerInvariant().Replace(' ', '_');
        if (SettingsManager.Current.SearchEngines.Any(en => en.Id == id))
            id += "_" + DateTime.Now.Ticks % 10000;

        SettingsManager.Current.SearchEngines.Add(new SearchEngine
        {
            Id = id, Name = name, UrlTemplate = url,
            Enabled = true, IsBuiltIn = false
        });

        CustomNameBox.Text = "";
        CustomUrlBox.Text = "";
        BuildSearchEnginesList();
    }

    private void ExcludedApps_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        SettingsManager.Current.ExcludedApps = ExcludedAppsBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
    }

    private void Save_Click(object sender, RoutedEventArgs e) =>
        Task.Run(() => SettingsManager.Save());

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(() => SettingsManager.Save());
        Close();
    }
}
