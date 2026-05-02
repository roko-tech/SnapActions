using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SnapActions.Config;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SnapActions.UI;

public partial class SettingsWindow : Window
{
    private bool _loading = true;
    private Brush? _textBrush;
    private Brush? _secondaryBrush;
    private readonly DispatcherTimer _saveDebounce;

    public SettingsWindow()
    {
        InitializeComponent();
        _textBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0xD6, 0xF4));
        _secondaryBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xAD, 0xC8));
        _textBrush.Freeze();
        _secondaryBrush.Freeze();

        // Debounce auto-save so a fast typer in the excluded-apps box doesn't trigger 30 disk writes.
        // Save synchronously on the UI thread — settings are tiny (kilobytes) and the previous
        // Task.Run could race with a UI mutation (List.Add etc.), throwing inside JsonSerializer
        // and silently losing the save.
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SettingsManager.Save();
        };

        // Flush any pending change immediately on close so users don't lose edits.
        // Save synchronously so it can't lose the race against process exit if the user
        // closes Settings and then immediately Exits from the tray menu.
        Closing += (_, _) =>
        {
            if (_saveDebounce.IsEnabled)
            {
                _saveDebounce.Stop();
                SettingsManager.Save();
            }
        };

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

    private void QueueSave()
    {
        if (_loading) return;
        _saveDebounce.Stop();
        _saveDebounce.Start();
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
        SelectComboByTag(LongPressCombo, s.LongPressDuration.ToString(), 1);
        SelectComboByTag(MaxInlineCombo, s.MaxInlineContextActions.ToString(), 3);
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
        QueueSave();
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enable = AutoStartCheck.IsChecked == true;
        // SetAutoStart already calls Save internally.
        Task.Run(() => SettingsManager.SetAutoStart(enable));
    }

    private void ShowDelay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ShowDelayCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
            SettingsManager.Current.ToolbarShowDelay = ms;
        QueueSave();
    }

    private void MultiClick_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (MultiClickCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
            SettingsManager.Current.MultiClickDelay = ms;
        QueueSave();
    }

    private void LongPress_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LongPressCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
            SettingsManager.Current.LongPressDuration = ms;
        QueueSave();
    }

    private void MaxInline_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (MaxInlineCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int n))
            SettingsManager.Current.MaxInlineContextActions = n;
        QueueSave();
    }

    private void DismissTime_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (DismissTimeCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int ms))
            SettingsManager.Current.ToolbarDismissTimeout = ms;
        QueueSave();
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item)
            SettingsManager.Current.SearchLanguage = item.Tag?.ToString() ?? "";
        QueueSave();
    }

    private void Currency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CurrencyCombo.SelectedItem is ComboBoxItem item)
            SettingsManager.Current.TargetCurrency = item.Tag?.ToString() ?? "USD";
        QueueSave();
    }

    private void EngineToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string id }) return;
        var engine = SettingsManager.Current.SearchEngines.FirstOrDefault(en => en.Id == id);
        if (engine != null) engine.Enabled = ((CheckBox)sender).IsChecked == true;
        QueueSave();
    }

    private void LangToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string id }) return;
        var engine = SettingsManager.Current.SearchEngines.FirstOrDefault(en => en.Id == id);
        if (engine != null) engine.UseLanguageFilter = ((CheckBox)sender).IsChecked == true;
        QueueSave();
    }

    private void DeleteEngine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        SettingsManager.Current.SearchEngines.RemoveAll(en => en.Id == id);
        BuildSearchEnginesList();
        QueueSave();
    }

    private void AddCustomEngine_Click(object sender, RoutedEventArgs e)
    {
        var name = CustomNameBox.Text.Trim();
        var url = CustomUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

        // Validate URL — only http/https allowed, must be a parseable absolute URL.
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Custom search engine URLs must start with http:// or https://",
                "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // {0} placeholder is the URL-encoded query — accept it as a placeholder during validation.
        var probeUrl = url.Replace("{0}", "test").Replace("{1}", "en");
        if (!Uri.TryCreate(probeUrl, UriKind.Absolute, out _))
        {
            MessageBox.Show("Custom search engine URL is not valid.",
                "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
        QueueSave();
    }

    private void ExcludedApps_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        SettingsManager.Current.ExcludedApps = ExcludedAppsBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        QueueSave();
    }

    // Settings auto-save on every change; this button is now an explicit "save now" if the user
    // wants to force-flush before any debounce timer fires. Synchronous so the user can be sure
    // it's persisted by the time the click handler returns.
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _saveDebounce.Stop();
        SettingsManager.Save();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void AddRunningApp_Click(object sender, RoutedEventArgs e)
    {
        // List currently-running processes that have a visible main window — typing process
        // names into the textbox by hand is error-prone (case, spelling, .exe vs not).
        var picker = new System.Windows.Window
        {
            Title = "Pick an app to exclude",
            Width = 320, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x2E)),
        };
        var list = new System.Windows.Controls.ListBox
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x3D)),
            Foreground = _textBrush,
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x3D, 0x50)),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Margin = new Thickness(8),
        };

        try
        {
            var ownPid = Environment.ProcessId;
            var existing = new HashSet<string>(SettingsManager.Current.ExcludedApps,
                StringComparer.OrdinalIgnoreCase);
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.Id == ownPid) continue;
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    var name = p.ProcessName; // already without .exe
                    if (string.IsNullOrEmpty(name)) continue;
                    if (existing.Contains(name)) continue;
                    if (name.Equals("SnapActions", StringComparison.OrdinalIgnoreCase)) continue;
                    names.Add(name);
                }
                catch { /* access denied on system processes — skip */ }
                finally { p.Dispose(); }
            }
            foreach (var n in names) list.Items.Add(n);
        }
        catch (Exception ex)
        {
            SnapActions.Helpers.Log.Warn($"Process enumeration failed: {ex.Message}");
        }

        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is string s) AddExcludedAppName(s);
            picker.Close();
        };

        var addBtn = new System.Windows.Controls.Button
        {
            Content = "Add", Padding = new Thickness(16, 4, 16, 4),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA)),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x2E)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
        };
        addBtn.Click += (_, _) =>
        {
            if (list.SelectedItem is string s) AddExcludedAppName(s);
            picker.Close();
        };
        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "Cancel", Padding = new Thickness(16, 4, 16, 4),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x3D)),
            Foreground = _textBrush,
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x3D, 0x50)),
        };
        cancelBtn.Click += (_, _) => picker.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 8, 8),
        };
        buttonRow.Children.Add(addBtn);
        buttonRow.Children.Add(cancelBtn);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(list, 0);
        Grid.SetRow(buttonRow, 1);
        grid.Children.Add(list);
        grid.Children.Add(buttonRow);
        picker.Content = grid;
        picker.ShowDialog();
    }

    private void AddExcludedAppName(string name)
    {
        var current = ExcludedAppsBox.Text;
        // Already-newline-terminated content (either \n or \r\n) doesn't need a leading separator.
        // The TextChanged handler splits on '\n', so CRLF endings would otherwise leave a trailing \r
        // attached to the previous entry.
        bool endsWithNewline = current.EndsWith('\n') || current.EndsWith("\r\n", StringComparison.Ordinal);
        var sep = string.IsNullOrEmpty(current) || endsWithNewline ? "" : "\n";
        ExcludedAppsBox.Text = current + sep + name;
        // The TextChanged handler will pick this up and queue a save.
    }
}
