using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using SnapActions.Helpers;

namespace SnapActions.Core;

public class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint NcHitTestTimeoutMs = 50;

    // Scrollbar suppression: a drag with both endpoints within this many px of the foreground
    // window's right (vertical scrollbar) or bottom (horizontal scrollbar) edge AND a strongly
    // perpendicular direction is treated as a scrollbar drag, not a text-selection drag.
    // Native scrollbars are already caught by NCHITTEST=HTVSCROLL/HTHSCROLL; this exists for
    // custom scrollbars (Chrome, VS Code, Electron apps) where NCHITTEST returns HTCLIENT.
    private const int ScrollbarEdgeSlopPx = 25;
    private const double PerpendicularRatio = 3.0;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYOUTRTL = 0x00400000;

    // GetSystemMetrics indices for the OS-defined click vs. drag thresholds. Same values
    // Windows uses internally for DragDetect() — keeping our thresholds in lockstep means we
    // distinguish "click" from "drag" the way every other Windows app does, and we respect
    // user / OS / DPI overrides automatically.
    private const int SM_CXDRAG = 68;       // half-width of the drag rectangle (typ. 4 px)
    private const int SM_CYDRAG = 69;       // half-height of the drag rectangle (typ. 4 px)
    private const int SM_CXDOUBLECLK = 36;  // half-width of the double-click rectangle (typ. 4 px)
    private const int SM_CYDOUBLECLK = 37;  // half-height of the double-click rectangle (typ. 4 px)

    /// <summary>
    /// Squared system drag threshold. A motion of more than √value pixels from the mouse-down
    /// point cancels the long-press timer. Read once at process start because system metrics
    /// don't change without a user-session restart.
    /// </summary>
    /// <remarks>
    /// Visible to tests so they can sanity-check the value is reasonable (typically 16 = 4²)
    /// without taking a hard dependency on a specific number.
    /// </remarks>
    internal static readonly int LongPressMoveCancelDistSq = ComputeSquaredThreshold(SM_CXDRAG, SM_CYDRAG, fallback: 4);

    /// <summary>
    /// Squared system double-click radius. Two clicks within √value pixels are treated as a
    /// multi-click cluster. Tighter than our old hardcoded 64 (8 px) so a slow drag onset
    /// between two clicks doesn't get misread as a double-click in the same spot.
    /// </summary>
    internal static readonly int MultiClickRadiusSq = ComputeSquaredThreshold(SM_CXDOUBLECLK, SM_CYDOUBLECLK, fallback: 4);

    // 10px² = 100 — minimum drag distance to count as a selection. Not a system metric — this
    // is our own "the user definitely intended to drag-select" floor, deliberately above the
    // drag-cancel threshold so a small click-then-twitch doesn't fire SelectionLikely.
    private const int MinDragSelectDistSq = 100;
    private const int MinClickDurationMs = 80;
    private const int MultiClickWindowMs = 500;

    /// <summary>
    /// max(cx, cy)² with a sane fallback when GetSystemMetrics returns 0 (e.g. headless / RDP
    /// during init). We use max rather than an ellipse for two reasons: cardinal-direction
    /// motion (just-x or just-y) gets the full allowance, and a single comparison against
    /// distSq keeps the hot path branch-free.
    /// </summary>
    private static int ComputeSquaredThreshold(int cxIndex, int cyIndex, int fallback)
    {
        int cx = GetSystemMetrics(cxIndex);
        int cy = GetSystemMetrics(cyIndex);
        int max = Math.Max(cx, cy);
        if (max <= 0) max = fallback;
        return max * max;
    }

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
    // Signaled once the hook thread has set _hookId and _hookDispatcher; lets Uninstall wait safely.
    private readonly ManualResetEventSlim _hookReady = new(false);

    /// <summary>What gesture produced a SelectionLikely fire.</summary>
    public enum SelectionTrigger
    {
        /// <summary>Mouse-up after a drag of at least <see cref="MinDragSelectDistSq"/> px².</summary>
        Drag,
        /// <summary>Double/triple-click cluster.</summary>
        MultiClick,
    }

    public event Action<POINT, SelectionTrigger>? SelectionLikely;
    public event Action<POINT>? LongPress;
    public event Action<POINT>? MouseDown;

    /// <summary>
    /// Static mirror of <see cref="MouseDown"/> for subscribers that don't hold the hook instance
    /// (same pattern as <see cref="KeyboardHook.EscPressed"/>). ResultPopup uses it for instant
    /// click-outside dismissal instead of polling. Fires on the hook thread — marshal before WPF.
    /// </summary>
    public static event Action<POINT>? GlobalMouseDown;

    public MouseHook()
    {
        _hookProc = HookCallback;
    }

    public void Install()
    {
        // Guard on _hookThread (set synchronously below) rather than _hookId (written later, on the
        // spawned thread). Reading the async-written _hookId here was a TOCTOU: two rapid Install()
        // calls could both pass it, spawn two hook threads, and leak the first hook.
        if (_hookThread != null) return;

        // Run the hook on a dedicated thread with its own message pump.
        // This prevents UI thread work (WPF layout, GC) from delaying hook callbacks.
        _hookThread = new Thread(() =>
        {
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
            if (_hookId == IntPtr.Zero)
                Log.Error($"Hook install failed: Win32 error {Marshal.GetLastWin32Error()}");
            else
                Log.Info($"Mouse hook installed on dedicated thread (id={_hookId})");

            _hookDispatcher = Dispatcher.CurrentDispatcher;

            _longPressTimer = new DispatcherTimer(DispatcherPriority.Normal, _hookDispatcher)
                { Interval = TimeSpan.FromMilliseconds(Config.SettingsManager.Current.LongPressDuration) };
            _longPressTimer.Tick += OnLongPressTimer;

            _multiClickTimer = new DispatcherTimer(DispatcherPriority.Normal, _hookDispatcher)
                { Interval = TimeSpan.FromMilliseconds(200) };
            _multiClickTimer.Tick += OnMultiClickTimer;

            // Tell Install/Uninstall the hook is ready to be controlled.
            _hookReady.Set();

            // Run message pump so the hook receives callbacks
            Dispatcher.Run();
        });
        _hookThread.IsBackground = true;
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    public void Uninstall()
    {
        // Wait for the hook thread to finish initializing before tearing it down — otherwise
        // we may try to call InvokeAsync on a null dispatcher and leak the hook.
        if (!_hookReady.Wait(2000))
            Log.Warn("Hook didn't become ready within 2s; tearing down anyway");

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
        _hookThread = null; // reset so a later Install() isn't no-op'd by the non-null guard
    }

    public void CancelTracking()
    {
        // CancelTracking is always called from the hook thread (from MouseHook events fired
        // synchronously inside ProcessMouseEvent), so DispatcherTimer.Stop is safe to call directly.
        _isTracking = false;
        _longPressTimer?.Stop();
        _multiClickTimer?.Stop();
    }

    private void OnMultiClickTimer(object? sender, EventArgs e)
    {
        _multiClickTimer?.Stop();
        // Deferred non-client gate — double-clicking a title bar (maximize) or border must not
        // read as a selection. See the WM_LBUTTONDOWN note for why this runs at fire time.
        if (_clickCount >= 2 && IsClickOnClientArea(_lastClickPoint))
        {
            try { SelectionLikely?.Invoke(_lastClickPoint, SelectionTrigger.MultiClick); }
            catch (Exception ex) { Log.Warn($"SelectionLikely (multi-click) handler threw: {ex.Message}"); }
        }
        _clickCount = 0;
    }

    private void OnLongPressTimer(object? sender, EventArgs e)
    {
        _longPressTimer?.Stop();
        if (!_isTracking) return;
        // Holding the mouse on a scrollbar (right/bottom slop region of the foreground window)
        // shouldn't summon paste mode. The downstream IsTextInputAtPoint check catches most of
        // these but Chrome's accessibility tree exposes the document under its custom scrollbar,
        // so the AutomationElement check returns true. Geometric edge check is a cheap safety
        // net that handles those.
        if (LooksLikeScrollbarPosition(_mouseDownPoint))
        {
            Log.Info($"Suppressed long-press: hold at ({_mouseDownPoint.X},{_mouseDownPoint.Y}) is in the scrollbar slop region");
            return;
        }
        // Deferred non-client gate — holding the button on a title bar / border must not summon
        // paste mode. See the WM_LBUTTONDOWN note for why this runs at fire time.
        if (!IsClickOnClientArea(_mouseDownPoint))
        {
            Log.Info($"Suppressed long-press: hold at ({_mouseDownPoint.X},{_mouseDownPoint.Y}) is on a non-client area");
            return;
        }
        _longPressFired = true;
        try { LongPress?.Invoke(_mouseDownPoint); }
        catch (Exception ex) { Log.Warn($"LongPress handler threw: {ex.Message}"); }
    }

    // Logged once per process so a recurring hook-thread bug doesn't spam the log file. Hot path —
    // fires per mouse event — so we'd rather miss subsequent occurrences than write 60 lines/sec.
    private static int _hookCallbackErrorLogged;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
                ProcessMouseEvent(wParam.ToInt32(), lParam);
        }
        catch (Exception ex)
        {
            if (Interlocked.CompareExchange(ref _hookCallbackErrorLogged, 1, 0) == 0)
                Log.Error("MouseHook.ProcessMouseEvent threw (logged once per process)", ex);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void ProcessMouseEvent(int msg, IntPtr lParam)
    {
        if (msg == WM_LBUTTONDOWN)
        {
            var pt = ReadPoint(lParam);
            try { MouseDown?.Invoke(pt); }
            catch (Exception ex) { Log.Warn($"MouseDown handler threw: {ex.Message}"); }
            try { GlobalMouseDown?.Invoke(pt); }
            catch (Exception ex) { Log.Warn($"GlobalMouseDown handler threw: {ex.Message}"); }

            // NOTE: the non-client (NCHITTEST) gate used to run right here, on EVERY mouse-down
            // system-wide — a synchronous cross-process SendMessageTimeout (bounded 50 ms) that
            // delayed click delivery to a busy target. It now runs at gesture-fire time instead
            // (drag mouse-up, multi-click fire, long-press fire), so only candidate selection
            // gestures pay for it. See IsClickOnClientArea.

            _mouseDownPoint = pt;
            _mouseDownTicks = Environment.TickCount64;
            _isTracking = true;
            _longPressFired = false;
            // Only arm the long-press timer when the user has configured it as the paste-mode
            // trigger. When the trigger is DoubleClick or Off, holding the button does nothing
            // — saving the timer work, and (more importantly) ruling out the long-press path
            // entirely so a stale setting read can't fire a paste menu the user has disabled.
            if (_longPressTimer != null
                && Config.SettingsManager.Current.PasteModeTrigger == Config.PasteModeTrigger.LongPress)
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
                // Same scrollbar suppression as long-press, but with a stronger signal: drag must
                // be primarily perpendicular to the edge (vertical scrollbar = vertical drag).
                if (LooksLikeScrollbarDrag(_mouseDownPoint, up))
                {
                    Log.Info($"Suppressed: drag from ({_mouseDownPoint.X},{_mouseDownPoint.Y}) to ({up.X},{up.Y}) looks like a scrollbar drag");
                    return;
                }
                // Deferred non-client gate (cheap geometric check above runs first): a drag that
                // STARTED on a title bar / border / native scrollbar is a window drag, not a
                // text selection. Checked here instead of at mouse-down so only real candidate
                // gestures pay the cross-process NCHITTEST round-trip.
                if (!IsClickOnClientArea(_mouseDownPoint))
                {
                    Log.Info($"Suppressed: drag started at ({_mouseDownPoint.X},{_mouseDownPoint.Y}) on a non-client area (title bar / border / scrollbar)");
                    return;
                }
                try { SelectionLikely?.Invoke(up, SelectionTrigger.Drag); }
                catch (Exception ex) { Log.Warn($"SelectionLikely (drag) handler threw: {ex.Message}"); }
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
                        if (_clickCount == 2 && IsClickOnClientArea(up))
                        {
                            try { SelectionLikely?.Invoke(up, SelectionTrigger.MultiClick); }
                            catch (Exception ex) { Log.Warn($"SelectionLikely (instant double-click) handler threw: {ex.Message}"); }
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

    private static bool LooksLikeScrollbarDrag(POINT down, POINT up)
    {
        if (!TryGetForegroundWindowRect(out var rect, out bool isRtl)) return false;
        return LooksLikeScrollbarDrag(down, up, rect, isRtl);
    }

    /// <summary>
    /// Pure-function variant for testability: caller supplies the foreground rect + RTL flag.
    /// True when both endpoints of the drag are within <see cref="ScrollbarEdgeSlopPx"/> of the
    /// vertical-scrollbar edge (right for LTR, left for RTL) AND the motion is primarily
    /// vertical, OR both are near the bottom edge AND the motion is primarily horizontal.
    /// That's a custom-scrollbar drag — native scrollbars are caught earlier by
    /// NCHITTEST=HTVSCROLL/HTHSCROLL.
    /// </summary>
    internal static bool LooksLikeScrollbarDrag(POINT down, POINT up, RECT rect, bool isRtl)
    {
        int absDx = Math.Abs(up.X - down.X);
        int absDy = Math.Abs(up.Y - down.Y);

        // Vertical scrollbar: right edge for LTR, left edge for RTL (Arabic / Hebrew layouts
        // and apps that explicitly set WS_EX_LAYOUTRTL).
        bool downNearVer, upNearVer;
        if (isRtl)
        {
            downNearVer = down.X <= rect.left + ScrollbarEdgeSlopPx;
            upNearVer = up.X <= rect.left + ScrollbarEdgeSlopPx;
        }
        else
        {
            downNearVer = down.X >= rect.right - ScrollbarEdgeSlopPx;
            upNearVer = up.X >= rect.right - ScrollbarEdgeSlopPx;
        }
        if (downNearVer && upNearVer && absDy > absDx * PerpendicularRatio) return true;

        bool downNearBottom = down.Y >= rect.bottom - ScrollbarEdgeSlopPx;
        bool upNearBottom = up.Y >= rect.bottom - ScrollbarEdgeSlopPx;
        if (downNearBottom && upNearBottom && absDx > absDy * PerpendicularRatio) return true;

        return false;
    }

    private static bool LooksLikeScrollbarPosition(POINT pt)
    {
        if (!TryGetForegroundWindowRect(out var rect, out bool isRtl)) return false;
        return LooksLikeScrollbarPosition(pt, rect, isRtl);
    }

    /// <summary>
    /// Pure-function variant for testability. Single-point check for long-press: don't fire if
    /// the mouse is held within the scrollbar slop region of the vertical-scrollbar edge (right
    /// for LTR, left for RTL) or the bottom edge. Looser than the drag check since we can't read
    /// direction for a hold; biased toward false positives in the slop region (a user wanting
    /// paste mode at the very edge of a text input has to click ~25 px inside).
    /// </summary>
    internal static bool LooksLikeScrollbarPosition(POINT pt, RECT rect, bool isRtl)
    {
        bool nearVer = isRtl
            ? pt.X <= rect.left + ScrollbarEdgeSlopPx
            : pt.X >= rect.right - ScrollbarEdgeSlopPx;
        return nearVer || pt.Y >= rect.bottom - ScrollbarEdgeSlopPx;
    }

    private static bool TryGetForegroundWindowRect(out RECT rect, out bool isRtl)
    {
        rect = default;
        isRtl = false;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        // WS_EX_LAYOUTRTL signals Arabic/Hebrew-style mirrored layouts; scrollbars flip to the
        // left edge. Most apps don't set this even in RTL locales (they handle mirroring
        // internally), so this catches the explicit cases without false positives elsewhere.
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        isRtl = (exStyle & WS_EX_LAYOUTRTL) != 0;
        return GetWindowRect(hwnd, out rect);
    }

    /// <summary>
    /// True when a click at <paramref name="pt"/> (screen coords) lands inside the client area
    /// of the window beneath. Anything else (title bar, scrollbar, resize border) is a window
    /// drag, not a text-selection drag — called at gesture-fire time to suppress it.
    /// </summary>
    /// <remarks>
    /// SendMessageTimeout is bounded by SMTO_ABORTIFHUNG + 50ms so a wedged target can't lock
    /// up the hook thread. On timeout we return true (permissive) — better to occasionally show
    /// the toolbar over a slow-responding window than to suppress legitimate selections.
    /// </remarks>
    private static bool IsClickOnClientArea(POINT pt)
    {
        IntPtr hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        // LPARAM packing for WM_NCHITTEST: low word = x, high word = y, both in screen coords.
        // Mask to 16 bits before shifting so a negative-on-one-monitor coordinate doesn't sign-
        // extend into the high word.
        IntPtr lParam = (IntPtr)(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
        IntPtr ret = SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam,
            SMTO_ABORTIFHUNG, NcHitTestTimeoutMs, out IntPtr result);
        if (ret == IntPtr.Zero) return true; // timeout / no permission — be permissive
        return result.ToInt32() == HTCLIENT;
    }

    public void Dispose()
    {
        Uninstall();
        _hookReady.Dispose();
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

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT pt);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    // Internal so SnapActions.Tests can construct synthetic RECTs for scrollbar-helper tests.
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int left, top, right, bottom; }
}
