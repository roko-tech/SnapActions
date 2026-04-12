using System.Runtime.InteropServices;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class DeleteTextAction : IAction
{
    public string Id => "delete_text";
    public string Name => "Delete";
    public string IconKey => "IconContext";
    public ActionCategory Category => ActionCategory.Transform;

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        // Send Delete key to remove the selected text
        var inputs = new INPUT[2];
        inputs[0] = MakeKeyInput(0x2E, false); // VK_DELETE down
        inputs[1] = MakeKeyInput(0x2E, true);  // VK_DELETE up
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        return new ActionResult(true, Message: "Deleted");
    }

    private static INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        var input = new INPUT { type = 1 }; // INPUT_KEYBOARD
        input.u.ki.wVk = vk;
        input.u.ki.dwFlags = keyUp ? 0x0002u : 0;
        return input;
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
