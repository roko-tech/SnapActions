using System.Runtime.InteropServices;

namespace SnapActions.Core;

/// <summary>
/// What the current mouse cursor tells us about the pointer target. Used to gate the selection
/// toolbar so it doesn't appear after non-text gestures (dragging a file / window, clicking a
/// button, panning a canvas) — the OS shows the text (I-beam) cursor only over selectable text,
/// which is a more universal signal than UIA TextPattern (uneven app coverage).
/// </summary>
public enum CursorKind
{
    /// <summary>The shared system I-beam — the pointer is over selectable text.</summary>
    TextIBeam,
    /// <summary>A known non-text system cursor (arrow, hand, resize, wait, …) — definitely not text.</summary>
    KnownNonText,
    /// <summary>A custom cursor we can't identify. Some apps draw their own I-beam, so this is
    /// NOT evidence against text — see <see cref="CursorShape.DecideCaptureAggressiveness"/>.</summary>
    Unknown,
    /// <summary>Cursor hidden (touch input, full-screen video) or the API failed — no signal at all.</summary>
    Unreadable,
}

/// <summary>How hard the capture pipeline may try for this gesture.</summary>
public enum CaptureAggressiveness
{
    /// <summary>Quiet mechanisms only (WM_COPY + UIA) — never inject a synthetic keystroke.
    /// Used when the cursor was a custom shape we can't classify: a custom I-beam deserves a
    /// capture attempt, but a custom game/canvas cursor must never receive a Ctrl+Insert.</summary>
    Quiet,
    /// <summary>Full cascade including the Ctrl+Insert last resort.</summary>
    Full,
}

public static class CursorShape
{
    private const int IDC_IBEAM = 32513;
    private const int CURSOR_SHOWING = 0x0001;

    // The shared system I-beam handle. LoadCursor(NULL, IDC_IBEAM) returns the same shared handle
    // GetCursorInfo reports when any app sets the standard I-beam, so a plain handle comparison
    // covers the common case. Cached once — a cursor-scheme change replaces the *contents* of the
    // shared handle, not the handle value, so the cache survives scheme switches.
    private static readonly IntPtr SystemIBeam = LoadCursor(IntPtr.Zero, IDC_IBEAM);

    // Every other standard system cursor. A match here means the pointer is positively over a
    // non-text target (arrow, link hand, resize edge, busy, …). A cursor matching NONE of the
    // shared handles is some app's custom cursor — which may well be a custom I-beam (editors,
    // terminals, themed apps), so it must not be treated as "not text"; it classifies Unknown
    // and the pipeline falls back to quiet capture instead of suppressing outright.
    private static readonly IntPtr[] KnownNonTextCursors = BuildKnownNonTextCursors();

    private static IntPtr[] BuildKnownNonTextCursors()
    {
        // IDC_ARROW, IDC_WAIT, IDC_CROSS, IDC_UPARROW, IDC_SIZENWSE, IDC_SIZENESW, IDC_SIZEWE,
        // IDC_SIZENS, IDC_SIZEALL, IDC_NO, IDC_HAND, IDC_APPSTARTING, IDC_HELP
        int[] ids = [32512, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651];
        var handles = new List<IntPtr>(ids.Length);
        foreach (var id in ids)
        {
            var h = LoadCursor(IntPtr.Zero, id);
            if (h != IntPtr.Zero) handles.Add(h);
        }
        return handles.ToArray();
    }

    /// <summary>Classifies the cursor currently on screen. Never throws.</summary>
    public static CursorKind Classify()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci)) return CursorKind.Unreadable;            // API failure
        if ((ci.flags & CURSOR_SHOWING) == 0) return CursorKind.Unreadable;  // touch / full-screen
        if (ci.hCursor == IntPtr.Zero) return CursorKind.Unreadable;
        return ClassifyHandle(ci.hCursor);
    }

    /// <summary>Pure classification of a cursor handle — split out so tests can feed synthetic handles.</summary>
    internal static CursorKind ClassifyHandle(IntPtr hCursor)
    {
        if (hCursor == SystemIBeam) return CursorKind.TextIBeam;
        foreach (var known in KnownNonTextCursors)
            if (hCursor == known) return CursorKind.KnownNonText;
        return CursorKind.Unknown;
    }

    /// <summary>
    /// Gate policy for a selection gesture given the cursor at mouse-down and at gesture end.
    /// Returns null to suppress entirely, otherwise how aggressively capture may run.
    /// <list type="bullet">
    ///   <item>I-beam at either point → full cascade (the press or the release was on text).</item>
    ///   <item>Unreadable at either point → full cascade — matches the long-standing permissive
    ///         rule for touch / hidden-cursor selections (no signal must never suppress).</item>
    ///   <item>Known non-text cursor at BOTH points → suppress. This is the drag-a-file /
    ///         click-a-button / resize case the gate exists for, and it only fires on positive
    ///         identification of standard system cursors.</item>
    ///   <item>Anything else (a custom cursor somewhere, no I-beam seen) → quiet capture:
    ///         WM_COPY + UIA may run, synthetic keystrokes may not. Custom I-beams (editors,
    ///         terminals, themed apps) get their toolbar back; custom game/canvas cursors get,
    ///         at worst, a silent no-op probe.</item>
    /// </list>
    /// </summary>
    internal static CaptureAggressiveness? DecideCaptureAggressiveness(CursorKind atDown, CursorKind atUp)
    {
        if (atDown == CursorKind.TextIBeam || atUp == CursorKind.TextIBeam) return CaptureAggressiveness.Full;
        if (atDown == CursorKind.Unreadable || atUp == CursorKind.Unreadable) return CaptureAggressiveness.Full;
        if (atDown == CursorKind.KnownNonText && atUp == CursorKind.KnownNonText) return null;
        return CaptureAggressiveness.Quiet;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public int ptScreenPosX;
        public int ptScreenPosY;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
}
