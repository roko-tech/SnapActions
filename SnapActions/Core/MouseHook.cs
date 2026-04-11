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

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelMouseProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private POINT _mouseDownPoint;
    private DateTime _mouseDownTime;
    private bool _isTracking;

    private readonly DispatcherTimer _longPressTimer;
    private bool _longPressFired;

    // Multi-click tracking (double/triple click)
    private DateTime _lastClickTime = DateTime.MinValue;
    private POINT _lastClickPoint;
    private int _clickCount;
    private readonly DispatcherTimer _multiClickTimer;
    private POINT _pendingClickPoint;

    public event Action<POINT>? SelectionLikely;
    public event Action<POINT>? LongPress;
    public event Action<POINT>? MouseDown;

    public MouseHook()
    {
        _hookProc = HookCallback;
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _longPressTimer.Tick += OnLongPressTimer;
        _multiClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _multiClickTimer.Tick += OnMultiClickTimer;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
        Trace.WriteLine(_hookId == IntPtr.Zero
            ? $"[SnapActions] Hook failed: {Marshal.GetLastWin32Error()}"
            : $"[SnapActions] Hook installed: {_hookId}");
    }

    public void Uninstall()
    {
        _longPressTimer.Stop();
        _multiClickTimer.Stop();
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public void CancelTracking()
    {
        _longPressTimer.Stop();
        _multiClickTimer.Stop();
        _isTracking = false;
    }

    private void OnMultiClickTimer(object? sender, EventArgs e)
    {
        _multiClickTimer.Stop();
        if (_clickCount >= 2)
        {
            Trace.WriteLine($"[SnapActions] {(_clickCount >= 3 ? "Triple" : "Double")}-click select");
            try { SelectionLikely?.Invoke(_pendingClickPoint); } catch { }
        }
        _clickCount = 0;
    }

    private void OnLongPressTimer(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (!_isTracking) return;
        _longPressFired = true;
        try { LongPress?.Invoke(_mouseDownPoint); }
        catch (Exception ex) { Trace.WriteLine($"[SnapActions] LongPress handler error: {ex.Message}"); }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // CRITICAL: entire callback wrapped in try-catch.
        // If an exception escapes, Windows silently removes the hook permanently.
        try
        {
            if (nCode >= 0)
                ProcessMouseEvent(wParam.ToInt32(), lParam);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SnapActions] Hook callback error: {ex.Message}");
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ProcessMouseEvent(int msg, IntPtr lParam)
    {
        if (msg == WM_LBUTTONDOWN)
        {
            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            try { MouseDown?.Invoke(hs.pt); } catch { }
            _mouseDownPoint = hs.pt;
            _mouseDownTime = DateTime.UtcNow;
            _isTracking = true;
            _longPressFired = false;
            _longPressTimer.Stop();
            _longPressTimer.Start();
        }
        else if (msg == WM_MOUSEMOVE && _isTracking && !_longPressFired)
        {
            // Read only the POINT (first 8 bytes) instead of marshaling the full struct
            int mx = Marshal.ReadInt32(lParam, 0);
            int my = Marshal.ReadInt32(lParam, 4);
            double dx = mx - _mouseDownPoint.X;
            double dy = my - _mouseDownPoint.Y;
            if (dx * dx + dy * dy > 64)
                _longPressTimer.Stop();
        }
        else if (msg == WM_LBUTTONUP && _isTracking)
        {
            _longPressTimer.Stop();
            _isTracking = false;

            if (_longPressFired) { _longPressFired = false; return; }

            var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var up = hs.pt;
            var now = DateTime.UtcNow;
            double dx = up.X - _mouseDownPoint.X;
            double dy = up.Y - _mouseDownPoint.Y;
            double distSq = dx * dx + dy * dy;
            double dur = (now - _mouseDownTime).TotalMilliseconds;

            if (distSq >= 100 && dur >= 80)
            {
                Trace.WriteLine($"[SnapActions] Drag select");
                try { SelectionLikely?.Invoke(up); } catch { }
                _clickCount = 0;
                _lastClickTime = DateTime.MinValue;
            }
            else if (distSq < 64)
            {
                double cdx = up.X - _lastClickPoint.X;
                double cdy = up.Y - _lastClickPoint.Y;
                double since = (now - _lastClickTime).TotalMilliseconds;

                if (since < 500 && cdx * cdx + cdy * cdy < 64)
                {
                    _clickCount++;
                    _pendingClickPoint = up;
                    // Restart timer - wait to see if more clicks follow
                    _multiClickTimer.Stop();
                    _multiClickTimer.Start();
                }
                else
                {
                    _clickCount = 1;
                }
                _lastClickTime = now;
                _lastClickPoint = up;
            }
        }
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

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
