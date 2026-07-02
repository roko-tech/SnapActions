using System.Runtime.InteropServices;
using SnapActions.Core;
using SnapActions.Detection.Detectors;
using Xunit;

namespace SnapActions.Tests;

/// <summary>
/// Pins the pure gate-policy functions extracted in v2.1.0: which capture layers run for a given
/// UIA probe outcome (TextCapture.DecidePlan), how the cursor shapes at press/release gate a
/// gesture (CursorShape.DecideCaptureAggressiveness), and which paths are safe to existence-probe
/// synchronously (FilePathDetector.IsProbeSafe).
/// </summary>
public class CapturePolicyTests
{
    // ── TextCapture.DecidePlan ───────────────────────────────────────────────

    [Fact]
    public void Plan_ItemElement_RunsNothing()
    {
        // Explorer file / desktop icon / list row — WM_COPY would "succeed" by copying the
        // item's name, so every layer must stay off.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.SuppressItemElement,
            isDrag: true, allowSyntheticKeys: true);
        Assert.Equal(new TextCapture.CapturePlan(false, false, false), plan);
    }

    [Fact]
    public void Plan_EmptyTextPattern_Drag_KeepsKeystrokeFallback()
    {
        // The lying-provider case: TextPattern reports empty against a drag that passed the
        // I-beam and distance gates. Full fallback (minus the redundant UIA re-walk) — a real
        // no-selection drag makes Ctrl+Insert a no-op anyway (sequence number won't change).
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.EmptyTextPattern,
            isDrag: true, allowSyntheticKeys: true);
        Assert.Equal(new TextCapture.CapturePlan(RunWmCopy: true, RunUia: false, RunKeystroke: true), plan);
    }

    [Fact]
    public void Plan_EmptyTextPattern_MultiClick_IsQuiet()
    {
        // Double-click is the gesture most prone to non-text false positives: WM_COPY only
        // (silent, self-gating), never a synthetic keystroke.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.EmptyTextPattern,
            isDrag: false, allowSyntheticKeys: true);
        Assert.Equal(new TextCapture.CapturePlan(RunWmCopy: true, RunUia: false, RunKeystroke: false), plan);
    }

    [Fact]
    public void Plan_QuietAggressiveness_NeverInjectsKeys()
    {
        // A quiet capture (custom/unknown cursor) must not inject keys regardless of outcome.
        foreach (var outcome in new[]
                 { TextCapture.SelectionProbeOutcome.EmptyTextPattern, TextCapture.SelectionProbeOutcome.Unknown })
        {
            Assert.False(TextCapture.DecidePlan(outcome, isDrag: true, allowSyntheticKeys: false).RunKeystroke);
            Assert.False(TextCapture.DecidePlan(outcome, isDrag: false, allowSyntheticKeys: false).RunKeystroke);
        }
    }

    [Fact]
    public void Plan_Unknown_RunsFullCascade()
    {
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.Unknown,
            isDrag: false, allowSyntheticKeys: true);
        Assert.Equal(new TextCapture.CapturePlan(RunWmCopy: true, RunUia: true, RunKeystroke: true), plan);
    }

    // ── CursorShape.DecideCaptureAggressiveness ─────────────────────────────

    [Theory]
    [InlineData(CursorKind.TextIBeam, CursorKind.KnownNonText)] // press on text, app swapped cursor after
    [InlineData(CursorKind.KnownNonText, CursorKind.TextIBeam)] // drag ended on text
    [InlineData(CursorKind.Unreadable, CursorKind.KnownNonText)] // touch / hidden cursor stays permissive
    [InlineData(CursorKind.KnownNonText, CursorKind.Unreadable)]
    public void Cursor_IBeamOrUnreadable_AllowsFullCapture(CursorKind down, CursorKind up) =>
        Assert.Equal(CaptureAggressiveness.Full, CursorShape.DecideCaptureAggressiveness(down, up));

    [Fact]
    public void Cursor_KnownNonTextAtBothPoints_Suppresses()
    {
        // Drag-a-file / click-a-button / resize: positively identified non-text cursors.
        Assert.Null(CursorShape.DecideCaptureAggressiveness(CursorKind.KnownNonText, CursorKind.KnownNonText));
    }

    [Theory]
    [InlineData(CursorKind.Unknown, CursorKind.Unknown)]      // app draws its own cursor throughout
    [InlineData(CursorKind.KnownNonText, CursorKind.Unknown)] // ended on a custom cursor
    [InlineData(CursorKind.Unknown, CursorKind.KnownNonText)]
    public void Cursor_UnknownCustomCursor_FallsBackToQuietCapture(CursorKind down, CursorKind up)
    {
        // Regression: custom I-beams (editors, terminals, themed apps) used to be treated the
        // same as an arrow cursor and lost the toolbar entirely. They now get a quiet capture.
        Assert.Equal(CaptureAggressiveness.Quiet, CursorShape.DecideCaptureAggressiveness(down, up));
    }

    // ── CursorShape.ClassifyHandle ───────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [Fact]
    public void ClassifyHandle_RecognizesSharedSystemCursors()
    {
        var ibeam = LoadCursor(IntPtr.Zero, 32513); // IDC_IBEAM
        var arrow = LoadCursor(IntPtr.Zero, 32512); // IDC_ARROW
        var hand = LoadCursor(IntPtr.Zero, 32649);  // IDC_HAND
        Assert.Equal(CursorKind.TextIBeam, CursorShape.ClassifyHandle(ibeam));
        Assert.Equal(CursorKind.KnownNonText, CursorShape.ClassifyHandle(arrow));
        Assert.Equal(CursorKind.KnownNonText, CursorShape.ClassifyHandle(hand));
    }

    [Fact]
    public void ClassifyHandle_UnknownHandle_IsUnknownNotNonText()
    {
        // A handle matching no shared system cursor is some app's custom cursor — possibly a
        // custom I-beam — and must classify Unknown (quiet capture), not KnownNonText (suppress).
        Assert.Equal(CursorKind.Unknown, CursorShape.ClassifyHandle(new IntPtr(0x1234_5678)));
    }

    // ── FilePathDetector.IsProbeSafe ─────────────────────────────────────────

    [Theory]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\unreachable.example.invalid\share")]
    public void IsProbeSafe_UncPaths_AreNeverProbeSafe(string path) =>
        Assert.False(FilePathDetector.IsProbeSafe(path));

    [Fact]
    public void IsProbeSafe_LocalFixedDrive_IsProbeSafe()
    {
        // C: is a fixed drive on any machine these tests run on (dev box, CI runner).
        Assert.True(FilePathDetector.IsProbeSafe(@"C:\Windows\System32"));
    }
}
