using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using SnapActions.Helpers;

namespace SnapActions.Core;

/// <summary>
/// Global low-level keyboard hook for the single purpose of dismissing the toolbar / result
/// popup on Esc. Replaces the previous DispatcherTimer + GetAsyncKeyState polling — the polling
/// added up to 120 ms of Esc-to-dismiss latency and ran a 120 ms heartbeat whenever a window
/// was visible. This hook fires inline on the keystroke.
///
/// The hook runs on its own dedicated STA thread with its own dispatcher (same pattern as
/// <see cref="MouseHook"/>). Subscribers' handlers fire on the hook thread; marshal to the UI
/// dispatcher before touching WPF state.
/// </summary>
public static class KeyboardHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_CONTROL = 0x11;
    private const int VK_C = 0x43;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Hold the delegate in a static field so the GC can't collect it while it's pinned as the
    // hook procedure on the Win32 side.
    private static readonly LowLevelKeyboardProc _hookProc = HookCallback;

    private static IntPtr _hookId = IntPtr.Zero;
    private static Thread? _hookThread;
    private static Dispatcher? _hookDispatcher;
    private static readonly ManualResetEventSlim _hookReady = new(false);

    /// <summary>Fires on the hook thread whenever Esc is pressed anywhere in the OS.</summary>
    public static event Action? EscPressed;

    /// <summary>
    /// Fires on the hook thread when the user presses Ctrl+C. Fired BEFORE the keystroke reaches
    /// the foreground app (low-level hooks run first), so a subscriber can snapshot the pre-copy
    /// clipboard state. Marshal to the UI dispatcher before touching WPF.
    /// </summary>
    public static event Action? CtrlCPressed;

    public static void Install()
    {
        // Guard on _hookThread (set synchronously below), not _hookId (written later on the spawned
        // thread) — same TOCTOU fix as MouseHook.Install.
        if (_hookThread != null) return;

        _hookThread = new Thread(() =>
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            if (_hookId == IntPtr.Zero)
                Log.Error($"Keyboard hook install failed: Win32 error {Marshal.GetLastWin32Error()}");
            else
                Log.Info($"Keyboard hook installed on dedicated thread (id={_hookId})");

            _hookDispatcher = Dispatcher.CurrentDispatcher;
            _hookReady.Set();
            Dispatcher.Run();
        });
        _hookThread.IsBackground = true;
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    public static void Uninstall()
    {
        if (!_hookReady.Wait(2000))
            Log.Warn("Keyboard hook didn't become ready within 2s; tearing down anyway");

        _hookDispatcher?.InvokeAsync(() =>
        {
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

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    // KBDLLHOOKSTRUCT.vkCode is the first DWORD at lParam.
                    int vk = Marshal.ReadInt32(lParam, 0);
                    if (vk == VK_ESCAPE)
                    {
                        try { EscPressed?.Invoke(); } catch { /* don't break the hook chain */ }
                    }
                    else if (vk == VK_C && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
                    {
                        try { CtrlCPressed?.Invoke(); } catch { /* don't break the hook chain */ }
                    }
                }
            }
        }
        catch { /* don't break the hook chain */ }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
