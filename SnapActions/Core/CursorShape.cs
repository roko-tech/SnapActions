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
    /// <summary>An <b>unambiguous</b> non-text system cursor — resize (size-all/NS/WE/…), wait,
    /// crosshair, no-drop, help, up-arrow, app-starting. These mean the pointer is dragging /
    /// resizing / busy, never selecting text, so a gesture bracketed by them at both ends is
    /// suppressed outright.</summary>
    KnownNonText,
    /// <summary>The default <b>arrow</b> or the link <b>hand</b>. Both are non-text system cursors,
    /// but — unlike the resize/wait family — they routinely sit over genuinely selectable text in
    /// web and Electron content (a click-to-open tweet, an App Store description styled
    /// <c>cursor: default</c>). So they are NOT treated as proof-of-non-text: a gesture under them
    /// gets a quiet capture (UIA read, never a synthetic keystroke) and the UIA layer decides.
    /// See <see cref="CursorShape.DecideCaptureAggressiveness"/>.</summary>
    AmbiguousNonText,
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

    // Unambiguous non-text cursors: a match means the pointer is positively dragging / resizing /
    // busy — never selecting text. A gesture bracketed by these at both ends is suppressed.
    // Deliberately EXCLUDES arrow and hand (see AmbiguousNonTextCursors): those two are non-text
    // shapes that nonetheless commonly overlay selectable web/Electron text, so treating them as
    // proof-of-non-text was suppressing real selections (X/Twitter feed, App Store pages).
    private static readonly IntPtr[] HardNonTextCursors = LoadCursors(
        // IDC_WAIT, IDC_CROSS, IDC_UPARROW, IDC_SIZENWSE, IDC_SIZENESW, IDC_SIZEWE, IDC_SIZENS,
        // IDC_SIZEALL, IDC_NO, IDC_APPSTARTING, IDC_HELP
        [32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32650, 32651]);

    // The default arrow and the link hand. Non-text system cursors, but they sit over selectable
    // text constantly in browsers/Electron (click-to-open tweets, cards styled cursor:default /
    // cursor:pointer). Classified Ambiguous so the pipeline attempts a quiet UIA capture instead
    // of suppressing — a custom cursor matching NONE of the shared handles stays Unknown.
    private static readonly IntPtr[] AmbiguousNonTextCursors = LoadCursors(
        // IDC_ARROW, IDC_HAND
        [32512, 32649]);

    private static IntPtr[] LoadCursors(int[] ids)
    {
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
        foreach (var hard in HardNonTextCursors)
            if (hCursor == hard) return CursorKind.KnownNonText;
        foreach (var ambiguous in AmbiguousNonTextCursors)
            if (hCursor == ambiguous) return CursorKind.AmbiguousNonText;
        return CursorKind.Unknown;
    }

    /// <summary>
    /// Gate policy for a selection gesture given the cursor at mouse-down and at gesture end.
    /// Returns null to suppress entirely, otherwise how aggressively capture may run.
    /// <list type="bullet">
    ///   <item>I-beam at either point → full cascade (the press or the release was on text).</item>
    ///   <item>Unreadable at either point → full cascade — matches the long-standing permissive
    ///         rule for touch / hidden-cursor selections (no signal must never suppress).</item>
    ///   <item><b>Hard</b> non-text cursor (resize / wait / crosshair / no-drop / …) at BOTH points
    ///         → suppress. This is the resize-a-window / drag-a-thing / busy case: those gestures
    ///         never select text, so bail before any capture work.</item>
    ///   <item>Everything else → quiet capture: UIA may run, synthetic keystrokes may not. This now
    ///         includes the <b>arrow / hand</b> (<see cref="CursorKind.AmbiguousNonText"/>) case —
    ///         previously a hard suppress, which silently killed real selections over click-to-open
    ///         web text. A quiet capture lets the UIA layer read the selection while guaranteeing no
    ///         stray keystroke lands on a button / canvas. Custom I-beams (editors, terminals) and
    ///         custom game cursors (<see cref="CursorKind.Unknown"/>) behave as before.</item>
    /// </list>
    /// Mixed pairs — a hard non-text cursor at one end and arrow/hand (or unknown) at the other,
    /// e.g. a resize drag that clips an arrow region — intentionally fall through to Quiet rather
    /// than suppress. Those gestures select no text, so a quiet capture is a silent no-op; keeping
    /// the hard-suppress to the unambiguous BOTH-hard case avoids widening suppression onto the
    /// arrow/hand family the fix is meant to rescue.
    /// </summary>
    internal static CaptureAggressiveness? DecideCaptureAggressiveness(CursorKind atDown, CursorKind atUp)
    {
        if (atDown == CursorKind.TextIBeam || atUp == CursorKind.TextIBeam) return CaptureAggressiveness.Full;
        if (atDown == CursorKind.Unreadable || atUp == CursorKind.Unreadable) return CaptureAggressiveness.Full;
        if (atDown == CursorKind.KnownNonText && atUp == CursorKind.KnownNonText) return null;
        return CaptureAggressiveness.Quiet;
    }

    /// <summary>
    /// True when the cursor was the ambiguous arrow/hand at BOTH sample points. The capture layer
    /// uses this to withhold WM_COPY when UIA can't confirm a text selection (outcome Unknown):
    /// arrow/hand + no UIA text is almost always a genuine non-text item (an Explorer row seen
    /// during a UIA timeout), and WM_COPY there would copy the item's name and pop a spurious
    /// toolbar — the false positive the old hard-suppress prevented. Real web text always yields
    /// HasText/EmptyTextPattern (never Unknown), so the selection fix is unaffected; custom-cursor
    /// (Unknown-kind) quiet captures keep their WM_COPY fallback because they are not ambiguous.
    /// </summary>
    internal static bool IsAmbiguousBothPoints(CursorKind atDown, CursorKind atUp) =>
        atDown == CursorKind.AmbiguousNonText && atUp == CursorKind.AmbiguousNonText;

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
