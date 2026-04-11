using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace SnapActions.Core;

public static class TextCapture
{
    private const int INPUT_KEYBOARD = 1;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_INSERT = 0x2D;  // Ctrl+Insert = Copy (avoids letter-key hooks)
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_COPY = 0x0301;

    private static readonly IntPtr SNAPACTIONS_MARKER = new(0x534E4150);

    private static readonly INPUT[] CtrlVInputs = BuildKeyCombo(VK_CONTROL, VK_V);
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public static async Task<string?> CaptureSelectedTextAsync()
    {
        try
        {
            // Save current clipboard so we can restore it after
            string? saved = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { return null; }
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { Clipboard.Clear(); } catch { }
            });

            // Try WM_COPY first (no keyboard events)
            CopyViaWindowMessage();
            await Task.Delay(20);
            var text = await ReadClipboard();

            // Fall back to Ctrl+Insert (for browsers)
            if (string.IsNullOrEmpty(text))
            {
                CopyViaKeyboard();
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(10);
                    text = await ReadClipboard();
                    if (!string.IsNullOrEmpty(text)) break;
                }
            }

            // Restore original clipboard
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (saved != null) Clipboard.SetText(saved);
                    else Clipboard.Clear();
                }
                catch { }
            });

            return text;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SnapActions] Capture error: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> ReadClipboard()
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch { return null; }
        });
    }

    private static void CopyViaWindowMessage()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        uint threadId = GetWindowThreadProcessId(hwnd, out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };

        IntPtr target = hwnd;
        if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
            target = info.hwndFocus;

        SendMessage(target, WM_COPY, IntPtr.Zero, IntPtr.Zero);
    }

    private static void CopyViaKeyboard()
    {
        // Use Ctrl+Insert instead of Ctrl+C.
        // Browser extensions (like h5player) hook letter keys but not Insert.
        var inputs = BuildKeyCombo(VK_CONTROL, VK_INSERT);
        SendInput((uint)inputs.Length, inputs, InputSize);
    }

    public static void SimulateCtrlV()
    {
        SendInput((uint)CtrlVInputs.Length, CtrlVInputs, InputSize);
    }

    private static INPUT[] BuildKeyCombo(ushort modifier, ushort key) =>
    [
        MakeKeyInput(modifier, false),
        MakeKeyInput(key, false),
        MakeKeyInput(key, true),
        MakeKeyInput(modifier, true),
    ];

    private static INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = vk;
        input.u.ki.dwFlags = keyUp ? KEYEVENTF_KEYUP : 0;
        input.u.ki.dwExtraInfo = SNAPACTIONS_MARKER;
        return input;
    }

    // P/Invoke structs
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
