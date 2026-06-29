using System.Runtime.InteropServices;

namespace SnapActions.Helpers;

public static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>Increments every time the clipboard contents change — lets the capture path detect
    /// that a copy step actually wrote to the clipboard without having to clear it first, and lets
    /// the Ctrl+C trigger tell "user copied something" from "Ctrl+C with nothing selected".</summary>
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    // ── Power throttling opt-out (Windows 11 EcoQoS) ─────────────────────
    // A background tray app is a prime EcoQoS candidate; a throttled WH_MOUSE_LL/WH_KEYBOARD_LL
    // callback adds input latency and, if it exceeds LowLevelHooksTimeout, can be silently unhooked.
    // Opting out keeps hook-callback latency deterministic.
    private const int ProcessPowerThrottling = 4;                  // ProcessInformationClass
    private const uint POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint POWER_THROTTLING_EXECUTION_SPEED = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation, int processInformationSize);

    /// <summary>
    /// Clears Windows 11 EcoQoS / power-throttling for this process so hook callbacks keep
    /// deterministic latency. No-op on pre-1709 Windows (the API call simply fails, swallowed).
    /// To DISABLE throttling: set the EXECUTION_SPEED control bit with a 0 state (= run full speed).
    /// </summary>
    public static void TryDisablePowerThrottling()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = 0,
            };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
                Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch { /* older Windows / API unavailable — fine */ }
    }

    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public int type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = vk;
        input.u.ki.dwFlags = keyUp ? KEYEVENTF_KEYUP : 0;
        return input;
    }

    public static INPUT[] BuildKeyCombo(ushort modifier, ushort key) =>
    [
        MakeKeyInput(modifier, false),
        MakeKeyInput(key, false),
        MakeKeyInput(key, true),
        MakeKeyInput(modifier, true),
    ];
}
