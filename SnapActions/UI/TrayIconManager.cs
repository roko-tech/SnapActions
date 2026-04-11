using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using SnapActions.Config;

namespace SnapActions.UI;

public class TrayIconManager : IDisposable
{
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _contextMenu;
    private SettingsWindow? _settingsWindow;

    public void Initialize()
    {
        _contextMenu = new ContextMenuStrip();

        var enableItem = new ToolStripMenuItem("Enabled")
        {
            Checked = SettingsManager.Current.Enabled,
            CheckOnClick = true
        };
        enableItem.CheckedChanged += (_, _) =>
        {
            SettingsManager.Current.Enabled = enableItem.Checked;
            SettingsManager.Save();
        };

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();

        var autoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = SettingsManager.Current.AutoStart,
            CheckOnClick = true
        };
        autoStartItem.CheckedChanged += (_, _) =>
        {
            SettingsManager.SetAutoStart(autoStartItem.Checked);
        };

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        _contextMenu.Items.Add(enableItem);
        _contextMenu.Items.Add(autoStartItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(settingsItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Text = "SnapActions",
            Icon = CreateDefaultIcon(),
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _trayIcon.DoubleClick += (_, _) => ShowSettings();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static Icon CreateDefaultIcon()
    {
        // Try loading from app.ico first
        var exeDir = AppContext.BaseDirectory;
        var icoPath = System.IO.Path.Combine(exeDir, "app.ico");
        if (System.IO.File.Exists(icoPath))
        {
            try { return new Icon(icoPath, 16, 16); } catch { }
        }

        // Fallback: generate programmatically
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        using var accent = new SolidBrush(Color.FromArgb(137, 180, 250));
        using var dark = new SolidBrush(Color.FromArgb(30, 30, 46));
        g.Clear(Color.Transparent);
        g.FillRectangle(dark, 1, 1, 14, 14);
        // Cursor line
        g.FillRectangle(accent, 4, 3, 1, 10);
        // Text lines
        g.FillRectangle(accent, 6, 5, 7, 1);
        using var textBrush = new SolidBrush(Color.FromArgb(180, 205, 214, 244));
        g.FillRectangle(textBrush, 6, 8, 6, 1);
        g.FillRectangle(textBrush, 6, 11, 4, 1);
        // Green dot
        g.FillRectangle(new SolidBrush(Color.FromArgb(166, 227, 161)), 12, 11, 2, 2);

        IntPtr hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var clone = (Icon)icon.Clone();
        icon.Dispose();
        DestroyIcon(hIcon);
        return clone;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _contextMenu?.Dispose();
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
