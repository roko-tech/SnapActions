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
        _textBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0xD6, 0xF4));
        _secondaryBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xAD, 0xC8));
        _textBrush.Freeze();
        _secondaryBrush.Freeze();

        // Defer heavy loading to after render, use dispatcher idle priority
        ContentRendered += (_, _) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                LoadSettings();
                _loading = false;
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    private void LoadSettings()
    {
        var s = SettingsManager.Current;
        EnabledCheck.IsChecked = s.Enabled;
        AutoStartCheck.IsChecked = s.AutoStart;
        ReplaceSelectionCheck.IsChecked = s.ReplaceSelectionOnTransform;

        SelectComboByTag(DismissTimeCombo, s.ToolbarDismissTimeout.ToString(), 2);
        SelectComboByTag(ShowDelayCombo, s.ToolbarShowDelay.ToString(), 0);
        SelectComboByTag(MultiClickCombo, s.MultiClickDelay.ToString(), 2);
        SelectComboByTag(LanguageCombo, s.SearchLanguage, 0);
        SelectComboByTag(CurrencyCombo, s.TargetCurrency, 0);

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
                Foreground = _textBrush, Width = 130, Tag = engine.Id
            };
            cb.Checked += EngineToggle_Changed;
            cb.Unchecked += EngineToggle_Changed;
            row.Children.Add(cb);

            // "lang" checkbox: apply global language filter to this engine
            var langCb = new CheckBox
            {
                Content = "lang", IsChecked = engine.UseLanguageFilter,
                Foreground = _secondaryBrush, FontSize = 10, Tag = engine.Id,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Apply the selected Search Language to this engine"
            };
            langCb.Checked += LangToggle_Changed;
            langCb.Unchecked += LangToggle_Changed;
            row.Children.Add(langCb);

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

    private void MultiClick_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (MultiClickCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
            SettingsManager.Current.MultiClickDelay = ms;
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

    private void Currency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CurrencyCombo.SelectedItem is ComboBoxItem item)
            SettingsManager.Current.TargetCurrency = item.Tag?.ToString() ?? "USD";
    }

    private void EngineToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string id }) return;
        var engine = SettingsManager.Current.SearchEngines.FirstOrDefault(en => en.Id == id);
        if (engine != null) engine.Enabled = ((CheckBox)sender).IsChecked == true;
    }

    private void LangToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string id }) return;
        var engine = SettingsManager.Current.SearchEngines.FirstOrDefault(en => en.Id == id);
        if (engine != null) engine.UseLanguageFilter = ((CheckBox)sender).IsChecked == true;
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

        var baseId = "custom_" + name.ToLowerInvariant().Replace(' ', '_');
        var id = baseId;
        if (SettingsManager.Current.SearchEngines.Any(en => en.Id == id))
            id = baseId + "_" + Guid.NewGuid().ToString("N")[..8];

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

    // Settings auto-save on every change; this button just gives explicit confirmation.
    private void Save_Click(object sender, RoutedEventArgs e) =>
        Task.Run(() => SettingsManager.Save());

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
