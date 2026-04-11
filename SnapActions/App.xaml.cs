using System.Threading;
using System.Windows;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.UI;

namespace SnapActions;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TrayIconManager? _trayIcon;
    private SelectionTracker? _tracker;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "SnapActions_SingleInstance_Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("SnapActions is already running.", "SnapActions",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        SettingsManager.Load();

        _trayIcon = new TrayIconManager();
        _trayIcon.Initialize();

        _tracker = new SelectionTracker();
        _tracker.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tracker?.Stop();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
