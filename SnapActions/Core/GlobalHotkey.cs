using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SnapActions.Core;

internal sealed class GlobalHotkey : IDisposable
{
    private readonly HwndSource _window;
    private const int Id = 0x5341;
    internal static bool IsRegistered { get; private set; }

    internal GlobalHotkey(Action requested)
    {
        _window = new HwndSource(new HwndSourceParameters("SnapActions keyboard palette") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x0312 && wParam.ToInt32() == Id) { handled = true; requested(); }
            return IntPtr.Zero;
        });
        IsRegistered = RegisterHotKey(_window.Handle, Id, 0x4000 | 0x0002 | 0x0004, 0x20); // Ctrl+Shift+Space, no repeat
    }

    public void Dispose()
    {
        UnregisterHotKey(_window.Handle, Id);
        _window.Dispose();
        IsRegistered = false;
    }

    internal static bool ReturnToTarget(ForegroundTarget target)
    {
        if (!target.IsComplete || !IsWindow(target.ForegroundWindow)) return false;
        GetWindowThreadProcessId(target.ForegroundWindow, out var pid);
        return pid == target.ProcessId && SetForegroundWindow(target.ForegroundWindow);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
