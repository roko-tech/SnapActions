using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace SnapActions.Core;

public static class TextCapture
{
    private const int INPUT_KEYBOARD = 1;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_INSERT = 0x2D;  // Ctrl+Insert = Copy (avoids letter-key hooks)
    private const ushort VK_V = 0x56;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_COPY = 0x0301;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint WM_COPY_TIMEOUT_MS = 100;

    private static readonly INPUT[] CtrlInsertInputs = BuildCtrlInsertCombo();
    private static readonly INPUT[] CtrlVInputs = BuildKeyCombo(VK_CONTROL, VK_V);
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // Serialize captures so two rapid selections can't interleave snapshot/restore and corrupt the clipboard.
    private static readonly System.Threading.SemaphoreSlim _captureLock = new(1, 1);

    public static async Task<string?> CaptureSelectedTextAsync()
    {
        // Skip if a capture is already running — the caller will simply not show a toolbar this round.
        if (!await _captureLock.WaitAsync(0)) return null;
        try
        {
            // Snapshot ALL clipboard formats so images/files/RTF survive
            var saved = await Application.Current.Dispatcher.InvokeAsync(SnapshotClipboard);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { Clipboard.Clear(); } catch { }
            });

            // Try WM_COPY first (no keyboard events)
            CopyViaWindowMessage();
            await Task.Delay(20);
            var text = await ReadClipboard();

            // Fall back to Ctrl+Insert (for browsers). Up to 250 ms total.
            if (string.IsNullOrEmpty(text))
            {
                CopyViaKeyboard();
                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(10);
                    text = await ReadClipboard();
                    if (!string.IsNullOrEmpty(text)) break;
                }
            }

            // Restore original clipboard contents
            await Application.Current.Dispatcher.InvokeAsync(() => RestoreClipboard(saved));

            return text;
        }
        catch (Exception ex)
        {
            SnapActions.Helpers.Log.Error("Capture error", ex);
            return null;
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private static Dictionary<string, object>? SnapshotClipboard()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null) return null;
            var snap = new Dictionary<string, object>();
            foreach (var fmt in data.GetFormats(autoConvert: false))
            {
                try
                {
                    var obj = data.GetData(fmt, autoConvert: false);
                    if (obj != null) snap[fmt] = obj;
                }
                catch { /* delay-rendered formats may throw — skip */ }
            }
            return snap.Count == 0 ? null : snap;
        }
        catch { return null; }
    }

    private static void RestoreClipboard(Dictionary<string, object>? snapshot)
    {
        try
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                Clipboard.Clear();
                return;
            }
            var data = new System.Windows.DataObject();
            foreach (var (fmt, obj) in snapshot)
            {
                try { data.SetData(fmt, obj); } catch { }
            }
            Clipboard.SetDataObject(data, copy: true);
        }
        catch { }
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

        // Timeout-bounded so a hung target window can't block the dispatcher
        SendMessageTimeout(target, WM_COPY, IntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, WM_COPY_TIMEOUT_MS, out _);
    }

    private static void CopyViaKeyboard()
    {
        // Use Ctrl+Insert instead of Ctrl+C.
        // Browser extensions (like h5player) hook letter keys but not Insert.
        SendInput((uint)CtrlInsertInputs.Length, CtrlInsertInputs, InputSize);
    }

    public static void SimulateCtrlV()
    {
        SendInput((uint)CtrlVInputs.Length, CtrlVInputs, InputSize);
    }

    private static INPUT[] BuildKeyCombo(ushort modifier, ushort key) =>
    [
        MakeKeyInput(modifier, false, extended: false),
        MakeKeyInput(key, false, extended: false),
        MakeKeyInput(key, true, extended: false),
        MakeKeyInput(modifier, true, extended: false),
    ];

    // Insert is an extended key — without the flag some apps see numpad-0 instead.
    private static INPUT[] BuildCtrlInsertCombo() =>
    [
        MakeKeyInput(VK_CONTROL, false, extended: false),
        MakeKeyInput(VK_INSERT, false, extended: true),
        MakeKeyInput(VK_INSERT, true, extended: true),
        MakeKeyInput(VK_CONTROL, true, extended: false),
    ];

    private static INPUT MakeKeyInput(ushort vk, bool keyUp, bool extended)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = vk;
        uint flags = 0;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= KEYEVENTF_KEYUP;
        input.u.ki.dwFlags = flags;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
