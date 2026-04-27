using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SnapActions.Core;

public class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;

    // Tuning constants (squared distances in pixels, time in ms).
    // 8px² = 64 — radius below which we still consider the cursor "stationary" during a hold.
    private const int LongPressMoveCancelDistSq = 64;
    // 10px² = 100 — minimum drag distance to count as a selection.
    private const int MinDragSelectDistSq = 100;
    // 8px² = 64 — clicks within this radius of the previous one form a multi-click cluster.
    private const int MultiClickRadiusSq = 64;
    private const int MinClickDurationMs = 80;
    private const int MultiClickWindowMs = 500;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelMouseProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private POINT _mouseDownPoint;
    private long _mouseDownTicks;
    private bool _isTracking;

    // Long-press: fires on dedicated dispatcher, not UI thread
    private Dispatcher? _hookDispatcher;
    private DispatcherTimer? _longPressTimer;
    private bool _longPressFired;

    // Multi-click
    private long _lastClickTicks;
    private POINT _lastClickPoint;
    private int _clickCount;
    private DispatcherTimer? _multiClickTimer;

    // Hook thread
    private Thread? _hookThread;

    public event Action<POINT>? SelectionLikely;
    public event Action<POINT>? LongPress;
    public event Action<POINT>? MouseDown;

    public MouseHook()
    {
        _hookProc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        // Run the hook on a dedicated thread with its own message pump.
        // This prevents UI thread work (WPF layout, GC) from delaying hook callbacks.
        _hookThread = new Thread(() =>
        {
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
            Trace.WriteLine(_hookId == IntPtr.Zero
                ? $"[SnapActions] Hook failed: {Marshal.GetLastWin32Error()}"
                : $"[SnapActions] Hook installed on dedicated thread: {_hookId}");

            _hookDispatcher = Dispatcher.CurrentDispatcher;

            _longPressTimer = new DispatcherTimer(DispatcherPriority.Normal, _hookDispatcher)
                { Interval = TimeSpan.FromMilliseconds(Config.SettingsManager.Current.LongPressDuration) };
            _longPressTimer.Tick += OnLongPressTimer;

            _multiClickTimer = new DispatcherTimer(DispatcherPriority.Normal, _hookDispatcher)
                { Interval = TimeSpan.FromMilliseconds(200) };
            _multiClickTimer.Tick += OnMultiClickTimer;

            // Run message pump so the hook receives callbacks
            Dispatcher.Run();
        });
        _hookThread.IsBackground = true;
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    public void Uninstall()
    {
        _hookDispatcher?.InvokeAsync(() =>
        {
            _longPressTimer?.Stop();
            _multiClickTimer?.Stop();
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _hookDispatcher?.InvokeShutdown();
        });
        _hookThread?.Join(2000);
    }

    public void CancelTracking()
    {
        // Always route timer Stop calls through the hook dispatcher — DispatcherTimer requires it.
        // (Today this is invoked from the hook thread, which is the same dispatcher, so the
        // InvokeAsync is a no-op queue-up. But this keeps the contract explicit.)
        _hookDispatcher?.InvokeAsync(() =>
        {
            _longPressTimer?.Stop();
            _multiClickTimer?.Stop();
        });
        _isTracking = false;
    }

    private void OnMultiClickTimer(object? sender, EventArgs e)
    {
        _multiClickTimer?.Stop();
        if (_clickCount >= 2)
        {
            try { SelectionLikely?.Invoke(_lastClickPoint); } catch { }
        }
        _clickCount = 0;
    }

    private void OnLongPressTimer(object? sender, EventArgs e)
    {
        _longPressTimer?.Stop();
        if (!_isTracking) return;
        _longPressFired = true;
        try { LongPress?.Invoke(_mouseDownPoint); } catch { }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
                ProcessMouseEvent(wParam.ToInt32(), lParam);
        }
        catch { }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ProcessMouseEvent(int msg, IntPtr lParam)
    {
        if (msg == WM_LBUTTONDOWN)
        {
            var pt = ReadPoint(lParam);
            try { MouseDown?.Invoke(pt); } catch { }
            _mouseDownPoint = pt;
            _mouseDownTicks = Environment.TickCount64;
            _isTracking = true;
            _longPressFired = false;
            if (_longPressTimer != null)
            {
                _longPressTimer.Stop();
                // Re-read each press so settings changes apply without restart.
                _longPressTimer.Interval = TimeSpan.FromMilliseconds(
                    Config.SettingsManager.Current.LongPressDuration);
                _longPressTimer.Start();
            }
        }
        else if (msg == WM_MOUSEMOVE && _isTracking && !_longPressFired)
        {
            int mx = Marshal.ReadInt32(lParam, 0);
            int my = Marshal.ReadInt32(lParam, 4);
            double dx = mx - _mouseDownPoint.X;
            double dy = my - _mouseDownPoint.Y;
            if (dx * dx + dy * dy > LongPressMoveCancelDistSq)
                _longPressTimer?.Stop();
        }
        else if (msg == WM_LBUTTONUP && _isTracking)
        {
            _longPressTimer?.Stop();
            _isTracking = false;

            if (_longPressFired) { _longPressFired = false; return; }

            var up = ReadPoint(lParam);
            long dur = Environment.TickCount64 - _mouseDownTicks;
            double dx = up.X - _mouseDownPoint.X;
            double dy = up.Y - _mouseDownPoint.Y;
            double distSq = dx * dx + dy * dy;

            if (distSq >= MinDragSelectDistSq && dur >= MinClickDurationMs)
            {
                try { SelectionLikely?.Invoke(up); } catch { }
                _clickCount = 0;
                _lastClickTicks = 0;
            }
            else if (distSq < MultiClickRadiusSq)
            {
                long now = Environment.TickCount64;
                double cdx = up.X - _lastClickPoint.X;
                double cdy = up.Y - _lastClickPoint.Y;
                long since = now - _lastClickTicks;

                if (since < MultiClickWindowMs && cdx * cdx + cdy * cdy < MultiClickRadiusSq)
                {
                    _clickCount++;
                    if (Config.SettingsManager.Current.MultiClickDelay == 0)
                    {
                        // Instant: fire once on the first multi-click in the cluster.
                        // Subsequent clicks within the 500ms window are ignored so a triple-click
                        // doesn't fire SelectionLikely twice.
                        if (_clickCount == 2)
                        {
                            try { SelectionLikely?.Invoke(up); } catch { }
                        }
                    }
                    else
                    {
                        // Re-read setting each fire so changes take effect without restart
                        var delay = Config.SettingsManager.Current.MultiClickDelay;
                        if (_multiClickTimer != null)
                        {
                            _multiClickTimer.Stop();
                            _multiClickTimer.Interval = TimeSpan.FromMilliseconds(delay);
                            _multiClickTimer.Start();
                        }
                    }
                }
                else
                {
                    _clickCount = 1;
                }
                _lastClickTicks = now;
                _lastClickPoint = up;
            }
        }
    }

    private static POINT ReadPoint(IntPtr lParam) => new()
    {
        X = Marshal.ReadInt32(lParam, 0),
        Y = Marshal.ReadInt32(lParam, 4)
    };

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
