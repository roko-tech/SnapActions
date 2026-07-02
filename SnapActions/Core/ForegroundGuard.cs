namespace SnapActions.Core;

/// <summary>
/// Guards every synthetic-input path (paste, paste-plain, delete) against focus having moved
/// since the toolbar appeared. Armed with the foreground HWND at toolbar-show time; any path
/// about to inject input checks <see cref="StillValid"/> first, so an Alt-Tab — whether it
/// happens before or after the button click — can't redirect a keystroke into the wrong app.
/// (The toolbar itself is WS_EX_NOACTIVATE, so interacting with it never changes the foreground.)
/// Written and read on the UI dispatcher only.
/// </summary>
public static class ForegroundGuard
{
    private static IntPtr _expected;

    /// <summary>Snapshot the current foreground window as the intended input target.</summary>
    public static void Arm() => _expected = Helpers.NativeMethods.GetForegroundWindow();

    /// <summary>
    /// True when the foreground window is still the one the toolbar was shown for. Fail-open when
    /// either read came back null (no foreground window is a transient OS state, not an Alt-Tab).
    /// </summary>
    public static bool StillValid()
    {
        IntPtr current = Helpers.NativeMethods.GetForegroundWindow();
        return current == _expected || current == IntPtr.Zero || _expected == IntPtr.Zero;
    }
}
