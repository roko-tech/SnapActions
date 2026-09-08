using System.Windows;
using System.Windows.Controls;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.Helpers;
using SnapActions.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace SnapActions.UI;

public partial class SettingsWindow
{
    private void LoadAdditionalSettings()
    {
        var s = SettingsManager.Current;
        SelectComboByTag(ThemeCombo, s.Theme, 0);
        TranslationSourceCombo.ItemsSource = new[] { new LanguageOption("", "Detect language") }.Concat(LanguageOptions.All);
        TranslationTargetCombo.ItemsSource = LanguageOptions.All;
        TranslationSourceCombo.SelectedValue = s.TranslationSourceLanguage;
        TranslationTargetCombo.SelectedValue = s.TranslationTargetLanguage;
        DictionaryCombo.SelectedIndex = 0;
        if (s.DictionaryLanguage != "en")
        {
            DictionaryCombo.Items.Add(new ComboBoxItem { Content = s.DictionaryLanguage + " (unavailable)", Tag = s.DictionaryLanguage });
            DictionaryCombo.SelectedIndex = DictionaryCombo.Items.Count - 1;
        }
        AutoStartCheck.IsEnabled = !RuntimePaths.IsIsolated;
        BuildRecipesList();
        RefreshBrowserHealth();
    }

    private bool SaveWithStatus()
    {
        bool saved = SettingsManager.Save();
        SaveStatusText.Text = saved ? "Saved" : SettingsManager.LastSaveError;
        RetrySaveButton.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        return saved;
    }

    private void LookupLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SettingsManager.Current.TranslationSourceLanguage = TranslationSourceCombo.SelectedValue as string ?? "";
        SettingsManager.Current.TranslationTargetLanguage = TranslationTargetCombo.SelectedValue as string ?? "en";
        SettingsManager.Current.DictionaryLanguage = (DictionaryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
        QueueSave();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedItem is not ComboBoxItem item) return;
        SettingsManager.Current.Theme = item.Tag?.ToString() ?? "system";
        ThemeManager.Apply();
        _textBrush = (System.Windows.Media.Brush)FindResource("TextBrush");
        _secondaryBrush = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        _loading = true;
        BuildSearchEnginesList(); BuildUserActionsList(); BuildAppProfilesList(); BuildRecipesList();
        _loading = false;
        QueueSave();
    }

    private void SettingsSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (SettingsTabs == null) return;
        var terms = SettingsSearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int matches = 0;
        foreach (TabItem tab in SettingsTabs.Items)
        {
            var content = tab.Header + " " + SearchableText((DependencyObject)tab.Content);
            bool match = terms.All(t => content.Contains(t, StringComparison.OrdinalIgnoreCase));
            tab.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (match) matches++;
        }
        if (SettingsTabs.SelectedItem is not TabItem { Visibility: Visibility.Visible })
            SettingsTabs.SelectedItem = SettingsTabs.Items.Cast<TabItem>().FirstOrDefault(t => t.Visibility == Visibility.Visible);
        SearchStatusText.Text = terms.Length == 0 ? "" : matches == 0 ? "No matching settings" : $"{matches} section(s)";
    }

    private static string SearchableText(DependencyObject node)
    {
        string text = node switch
        {
            TextBlock block => block.Text,
            ContentControl { Content: string content } => content,
            _ => ""
        };
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) text += " " + SearchableText(child);
        return text;
    }

    private string SelectedBrowser => (BrowserCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Brave";
    private void Browser_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) RefreshBrowserHealth(); }
    private void RefreshBrowser_Click(object sender, RoutedEventArgs e) => RefreshBrowserHealth();
    private void RefreshBrowserHealth()
    {
        BrowserStatusText.Text = BrowserSetupService.Status(SelectedBrowser);
        DiagnosticsBox.Text = CaptureDiagnostics.Summary();
    }
    private void RegisterBrowser_Click(object sender, RoutedEventArgs e)
    {
        try { BrowserSetupService.Register(SelectedBrowser); RefreshBrowserHealth(); }
        catch (Exception ex) { BrowserStatusText.Text = "Registration failed: " + ex.Message; }
    }
    private void OpenExtension_Click(object sender, RoutedEventArgs e) => OpenSetupPath(BrowserSetupService.ExtensionDirectory);
    private void OpenBrowserSample_Click(object sender, RoutedEventArgs e) => OpenSetupPath(System.IO.Path.Combine(BrowserSetupService.ExtensionDirectory, "selection-sample.html"));
    private void OpenSetupPath(string path)
    {
        if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
        { BrowserStatusText.Text = "The companion files are missing. Extract the complete release package."; return; }
        var result = ProcessHelper.TryOpenLocalPath(path, "Opened");
        if (!result.Success) BrowserStatusText.Text = result.Message;
    }

    private void BuildRecipesList()
    {
        RecipesPanel.Children.Clear();
        foreach (var recipe in SettingsManager.Current.TextRecipes)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var remove = new Button { Content = "Delete", Padding = new Thickness(8, 3, 8, 3) };
            System.Windows.Automation.AutomationProperties.SetName(remove, "Delete recipe " + recipe.Name);
            remove.Click += (_, _) => { SettingsManager.Current.TextRecipes.Remove(recipe); BuildRecipesList(); QueueSave(); };
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var edit = new Button { Content = "Edit", Margin = new Thickness(8, 0, 8, 0), Padding = new Thickness(8, 3, 8, 3) };
            System.Windows.Automation.AutomationProperties.SetName(edit, "Edit recipe " + recipe.Name);
            edit.Click += (_, _) => EditRecipe(recipe);
            DockPanel.SetDock(edit, Dock.Right); row.Children.Add(edit);
            var enabled = new CheckBox { Content = recipe.Name, IsChecked = recipe.Enabled, VerticalAlignment = VerticalAlignment.Center };
            enabled.Checked += (_, _) => { recipe.Enabled = true; QueueSave(); };
            enabled.Unchecked += (_, _) => { recipe.Enabled = false; QueueSave(); };
            row.Children.Add(enabled); RecipesPanel.Children.Add(row);
        }
    }
    private void CreateRecipe_Click(object sender, RoutedEventArgs e) => EditRecipe(null);
    private void EditRecipe(TextRecipeDefinition? recipe)
    {
        var editor = new TextRecipeEditor(recipe) { Owner = this };
        if (editor.ShowDialog() != true) return;
        if (recipe == null) SettingsManager.Current.TextRecipes.Add(editor.Recipe);
        else { recipe.Name = editor.Recipe.Name; recipe.Steps = editor.Recipe.Steps; }
        BuildRecipesList(); QueueSave();
    }
}
