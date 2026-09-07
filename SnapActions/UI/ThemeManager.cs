using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SnapActions.Config;

namespace SnapActions.UI;

internal static class ThemeManager
{
    internal static void Start() { Apply(); SystemEvents.UserPreferenceChanged += OnPreferenceChanged; }
    internal static void Stop() => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
    private static void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (SettingsManager.Current.Theme == "system") Application.Current?.Dispatcher.InvokeAsync(Apply);
    }
    internal static void Apply()
    {
        bool light = SettingsManager.Current.Theme == "light";
        if (SettingsManager.Current.Theme == "system")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        string[] names = ["Background", "Surface", "Hover", "Active", "Border", "Text", "TextSecondary", "Accent", "Success", "Warning"];
        string[] colors = light
            ? ["#F7F8FB", "#FFFFFF", "#E5EAF3", "#D5DEED", "#AEB9CC", "#19243A", "#4E5D76", "#2255AC", "#236B35", "#98520B"]
            : ["#1E1E2E", "#2D2D3D", "#3D3D50", "#4D4D65", "#606B82", "#CDD6F4", "#A6ADC8", "#89B4FA", "#A6E3A1", "#FAB387"];
        for (int i = 0; i < names.Length; i++)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]);
            if (Application.Current.Resources[names[i] + "Brush"] is SolidColorBrush { IsFrozen: false } existing) existing.Color = color;
            else Application.Current.Resources[names[i] + "Brush"] = new SolidColorBrush(color);
        }
    }
}
