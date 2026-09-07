using System.Runtime.InteropServices;
using System.Windows;
using static SnapActions.Core.ClipboardTransaction;

namespace SnapActions.Core;

internal static class InputExecutor
{
    private const int INPUT_KEYBOARD = 1;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;    // Alt
    private const ushort VK_INSERT = 0x2D;  // Ctrl+Insert = Copy / Shift+Insert = Paste
    private const ushort VK_DELETE = 0x2E;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly KeyStroke[] ShiftInsertInputs = BuildExtendedInsertCombo(VK_SHIFT);
    private static readonly KeyStroke[] DeleteInputs =
    [
        new(VK_DELETE, KeyUp: false, Extended: true),
        new(VK_DELETE, KeyUp: true, Extended: true),
    ];
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();
    internal enum InputInjectionStatus
    {
        Rejected,
        Succeeded,
        Partial,
    }

    internal readonly record struct InputInjectionOutcome(
        InputInjectionStatus Status,
        bool CleanupSucceeded = true,
        uint AcceptedCount = 0);

    internal readonly record struct KeyStroke(
        ushort VirtualKey,
        bool KeyUp,
        bool Extended);
    internal static bool CanInjectAtBoundary(
        bool operationCurrent,
        ForegroundTarget expectedTarget,
        ForegroundTarget currentTarget,
        ClipboardObservation? expectedClipboard,
        ClipboardObservation currentClipboard) =>
        operationCurrent
        && ForegroundGuard.HasSufficientInputIdentity(expectedTarget)
        && ForegroundGuard.Matches(expectedTarget, currentTarget)
        && (expectedClipboard == null
            || CanRestoreClipboard(expectedClipboard.Value, currentClipboard));

    internal static bool CanRollbackAfterPartialPaste(
        InputInjectionOutcome outcome) =>
        outcome.Status == InputInjectionStatus.Partial
        && outcome.CleanupSucceeded
        && outcome.AcceptedCount < 2;

    /// <summary>
    /// Waits up to ~300 ms for the user to release the given modifier keys before we inject a
    /// synthetic chord. A modifier still held at gesture end (Shift+drag to extend a selection,
    /// Ctrl+drag for a discontiguous one) would otherwise corrupt the chord — Ctrl+Insert into
    /// Ctrl+Shift+Insert, Shift+Insert into Ctrl+Shift+Insert — which copies/pastes nothing in
    /// many apps. Destructive and clipboard-mutating chords require Shift, Ctrl, and Alt all to
    /// be released so our synthetic key-up cannot interfere with a physically held modifier.
    /// </summary>
    private static async Task<bool> WaitForModifierKeysReleasedAsync(params int[] vkeys)
    {
        for (int i = 0; i < 15; i++)
        {
            if (AreModifierKeysReleased(vkeys)) return true;
            await Task.Delay(20);
        }
        return false;
    }

    private static bool AreModifierKeysReleased(params int[] vkeys)
    {
        foreach (var vk in vkeys)
        {
            if ((SnapActions.Helpers.NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
                return false;
        }
        return true;
    }
    internal static async Task<bool> PreparePasteAsync(SelectionOperation operation)
    {
        if (!await operation.CanInjectInputAsync()) return false;
        return await WaitForModifierKeysReleasedAsync(VK_SHIFT, VK_CONTROL, VK_MENU)
               && await operation.CanInjectInputAsync();
    }

    internal static async Task<bool> PrepareDeleteAsync(SelectionOperation operation)
    {
        if (!await operation.CanInjectInputAsync()) return false;
        return await WaitForModifierKeysReleasedAsync(VK_SHIFT, VK_CONTROL, VK_MENU)
               && await operation.CanInjectInputAsync();
    }

    /// <summary>
    /// Sends Shift+Insert only if the immutable operation, exact input target, physical modifiers,
    /// and optional clipboard observation all still match at the final injection boundary.
    /// Call <see cref="PreparePasteAsync"/> before changing clipboard data.
    /// </summary>
    internal static Task<InputInjectionOutcome> TrySimulatePasteAsync(
        SelectionOperation operation, ClipboardObservation? expectedClipboard = null)
    {
        return TrySendInputAsync(
            operation,
            expectedClipboard,
            ShiftInsertInputs,
            VK_SHIFT, VK_CONTROL, VK_MENU);
    }

    internal static async Task<InputInjectionOutcome> SimulatePasteAsync(
        SelectionOperation operation)
    {
        var expectedClipboard = ObserveClipboard();
        if (!await PreparePasteAsync(operation))
            return new InputInjectionOutcome(InputInjectionStatus.Rejected);
        return await TrySimulatePasteAsync(operation, expectedClipboard);
    }

    internal static async Task<InputInjectionOutcome> SimulateDeleteAsync(
        SelectionOperation operation)
    {
        if (!await PrepareDeleteAsync(operation))
            return new InputInjectionOutcome(InputInjectionStatus.Rejected);
        return await TrySendInputAsync(
            operation,
            expectedClipboard: null,
            DeleteInputs,
            VK_SHIFT, VK_CONTROL, VK_MENU);
    }

    private static async Task<InputInjectionOutcome> TrySendInputAsync(
        SelectionOperation operation,
        ClipboardObservation? expectedClipboard,
        KeyStroke[] strokes,
        params int[] modifiersThatMustBeReleased)
    {
        var outcome = new InputInjectionOutcome(
            InputInjectionStatus.Rejected);
        bool reachedInputBoundary =
            await ForegroundGuard.TryRunWithExactInputTargetAsync(
            operation.Target,
            currentTarget =>
            {
                var currentClipboard = expectedClipboard == null
                    ? default
                    : ObserveClipboard();
                if (!CanInjectAtBoundary(
                        operation.IsCurrent,
                        operation.Target,
                        currentTarget,
                        expectedClipboard,
                        currentClipboard))
                    return false;
                if (!AreModifierKeysReleased(modifiersThatMustBeReleased))
                    return false;
                // Re-sample native identity immediately before SendInput. The UIA identity was
                // captured directly before this callback on the same worker.
                if (!ForegroundGuard.StillValid(operation.Target))
                    return false;
                // Repeat the claim after every potentially yielding or cross-process validation.
                // Hook-thread invalidation remains lock-free and wins before this final send point.
                if (!TrySendKeySequenceForOperation(
                        operation,
                        strokes,
                        SendNativeKeyStrokes,
                        out outcome))
                    return false;
                return true;
            });
        return reachedInputBoundary
            ? outcome
            : new InputInjectionOutcome(InputInjectionStatus.Rejected);
    }
    // Insert is an extended key — without the flag some apps see numpad-0 instead.
    private static KeyStroke[] BuildExtendedInsertCombo(ushort modifier) =>
    [
        new(modifier, KeyUp: false, Extended: false),
        new(VK_INSERT, KeyUp: false, Extended: true),
        new(VK_INSERT, KeyUp: true, Extended: true),
        new(modifier, KeyUp: true, Extended: false),
    ];

    internal static InputInjectionOutcome SendKeySequence(
        IReadOnlyList<KeyStroke> strokes,
        Func<IReadOnlyList<KeyStroke>, uint> sender)
    {
        // SendInput inserts an INPUT array serially and returns the inserted event count. For a
        // short prefix, synthesize key-up events for every accepted key-down not already paired
        // with an accepted key-up, in reverse press order.
        uint inserted = sender(strokes);
        if (inserted == (uint)strokes.Count)
            return new InputInjectionOutcome(
                InputInjectionStatus.Succeeded,
                AcceptedCount: inserted);
        if (inserted == 0)
            return new InputInjectionOutcome(InputInjectionStatus.Rejected);

        bool cleanupSucceeded = inserted < (uint)strokes.Count;
        if (cleanupSucceeded)
        {
            foreach (var release in BuildRecoveryKeyUps(strokes, inserted))
            {
                if (sender(new[] { release }) != 1)
                    cleanupSucceeded = false;
            }
        }

        return new InputInjectionOutcome(
            InputInjectionStatus.Partial,
            cleanupSucceeded,
            inserted);
    }

    internal static bool TrySendKeySequenceForOperation(
        SelectionOperation operation,
        IReadOnlyList<KeyStroke> strokes,
        Func<IReadOnlyList<KeyStroke>, uint> sender,
        out InputInjectionOutcome outcome)
    {
        outcome = new InputInjectionOutcome(InputInjectionStatus.Rejected);
        if (!operation.TryClaim()) return false;
        outcome = SendKeySequence(strokes, sender);
        return true;
    }

    private static IReadOnlyList<KeyStroke> BuildRecoveryKeyUps(
        IReadOnlyList<KeyStroke> strokes, uint inserted)
    {
        var pressed = new List<KeyStroke>();
        int accepted = Math.Min(strokes.Count, checked((int)inserted));
        for (int i = 0; i < accepted; i++)
        {
            var stroke = strokes[i];
            if (!stroke.KeyUp)
            {
                pressed.Add(stroke);
                continue;
            }

            int down = pressed.FindLastIndex(
                candidate => candidate.VirtualKey == stroke.VirtualKey);
            if (down >= 0) pressed.RemoveAt(down);
        }

        var releases = new List<KeyStroke>(pressed.Count);
        for (int i = pressed.Count - 1; i >= 0; i--)
        {
            var down = pressed[i];
            releases.Add(down with { KeyUp = true });
        }
        return releases;
    }

    private static uint SendNativeKeyStrokes(
        IReadOnlyList<KeyStroke> strokes)
    {
        var inputs = new INPUT[strokes.Count];
        for (int i = 0; i < strokes.Count; i++)
            inputs[i] = MakeKeyInput(strokes[i]);
        return SendInput((uint)inputs.Length, inputs, InputSize);
    }

    private static INPUT MakeKeyInput(KeyStroke stroke)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = stroke.VirtualKey;
        uint flags = 0;
        if (stroke.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (stroke.KeyUp) flags |= KEYEVENTF_KEYUP;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
