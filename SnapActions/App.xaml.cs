using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.Helpers;
using SnapActions.UI;

namespace SnapActions;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private TrayIconManager? _trayIcon;
    private SelectionTracker? _tracker;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "SnapActions_SingleInstance_Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            MessageBox.Show("SnapActions is already running.", "SnapActions",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Log unhandled exceptions on both UI and background threads — easier diagnosis
        // than the silent swallows we used to have everywhere.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true; // keep app alive
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled background exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info($"SnapActions starting (PID {Environment.ProcessId}, .NET {Environment.Version})");

        SettingsManager.Load();

        _trayIcon = new TrayIconManager();
        _trayIcon.Initialize();

        _tracker = new SelectionTracker();
        _tracker.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("SnapActions shutting down");
        _tracker?.Stop();
        _trayIcon?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
