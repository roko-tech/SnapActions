using System.Windows;
using System.Windows.Controls;
using SnapActions.Config;
using CheckBox = System.Windows.Controls.CheckBox;

namespace SnapActions.UI;

public partial class SettingsWindow : Window
{
    private bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
        _loading = false;
    }

    private void LoadSettings()
    {
        var s = SettingsManager.Current;
        EnabledCheck.IsChecked = s.Enabled;
        AutoStartCheck.IsChecked = s.AutoStart;
        ReplaceSelectionCheck.IsChecked = s.ReplaceSelectionOnTransform;

        // Dismiss time
        for (int i = 0; i < DismissTimeCombo.Items.Count; i++)
            if (DismissTimeCombo.Items[i] is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int val) && val == s.ToolbarDismissTimeout)
            { DismissTimeCombo.SelectedIndex = i; break; }
        if (DismissTimeCombo.SelectedIndex < 0) DismissTimeCombo.SelectedIndex = 2;

        ShowTransformCheck.IsChecked = s.ShowTransformActions;
        ShowEncodeCheck.IsChecked = s.ShowEncodeActions;
        ShowSearchCheck.IsChecked = s.ShowSearchActions;

        // Show delay
        for (int i = 0; i < ShowDelayCombo.Items.Count; i++)
            if (ShowDelayCombo.Items[i] is ComboBoxItem sdi &&
                int.TryParse(sdi.Tag?.ToString(), out int sdv) && sdv == s.ToolbarShowDelay)
            { ShowDelayCombo.SelectedIndex = i; break; }
        if (ShowDelayCombo.SelectedIndex < 0) ShowDelayCombo.SelectedIndex = 0;

        // Language
        for (int i = 0; i < LanguageCombo.Items.Count; i++)
            if (LanguageCombo.Items[i] is ComboBoxItem li &&
                li.Tag?.ToString() == s.SearchLanguage)
            { LanguageCombo.SelectedIndex = i; break; }
        if (LanguageCombo.SelectedIndex < 0) LanguageCombo.SelectedIndex = 0;

        BuildSearchEnginesList();

        ExcludedAppsBox.Text = string.Join("\n", s.ExcludedApps);
    }

    private void BuildSearchEnginesList()
    {
        SearchEnginesPanel.Children.Clear();
        foreach (var engine in SettingsManager.Current.SearchEngines)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            var cb = new CheckBox
            {
                Content = engine.Name,
                IsChecked = engine.Enabled,
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                Width = 150,
                Tag = engine.Id
            };
            cb.Checked += EngineToggle_Changed;
            cb.Unchecked += EngineToggle_Changed;
            row.Children.Add(cb);

            // Show URL hint for custom engines
            if (!engine.IsBuiltIn)
            {
                row.Children.Add(new TextBlock
                {
                    Text = engine.UrlTemplate.Length > 40
                        ? engine.UrlTemplate[..40] + "..."
                        : engine.UrlTemplate,
                    FontSize = 10,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                });

                var delBtn = new System.Windows.Controls.Button
                {
                    Content = "X",
                    Width = 22, Height = 22,
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    BorderThickness = new Thickness(0),
                    Tag = engine.Id,
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
        SettingsManager.SetAutoStart(AutoStartCheck.IsChecked == true);
    }

    private void ShowDelay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ShowDelayCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
        {
            SettingsManager.Current.ToolbarShowDelay = ms;
            }
    }

    private void DismissTime_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (DismissTimeCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
        {
            SettingsManager.Current.ToolbarDismissTimeout = ms;
            }
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item)
        {
            SettingsManager.Current.SearchLanguage = item.Tag?.ToString() ?? "";
            }
    }

    private void EngineToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string id }) return;
        var engine = SettingsManager.Current.SearchEngines.FirstOrDefault(en => en.Id == id);
        if (engine != null)
        {
            engine.Enabled = ((CheckBox)sender).IsChecked == true;
            }
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

        // Auto-add {0} if missing
        if (!url.Contains("{0}"))
            url += (url.Contains('?') ? "&" : "?") + "q={0}";

        var id = "custom_" + name.ToLowerInvariant().Replace(' ', '_');
        // Prevent duplicate IDs
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Save();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Save();
        Close();
    }
}
