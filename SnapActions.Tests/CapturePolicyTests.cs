using System.Runtime.InteropServices;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.Detection.Detectors;
using Xunit;

namespace SnapActions.Tests;

/// <summary>
/// Pins selection provenance, bidi geometry, cursor gesture gates, and paths safe to existence-probe
/// synchronously (FilePathDetector.IsProbeSafe).
/// </summary>
public class CapturePolicyTests
{
    [Fact]
    public void MouseSelectionCapture_DefaultsOn()
    {
        Assert.True(SelectionTracker.ShouldCaptureMouseSelection(new AppSettings()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouseSelectionCapture_RespectsSetting(bool enabled)
    {
        var settings = new AppSettings { CaptureOnMouseSelection = enabled };

        Assert.Equal(enabled, SelectionTracker.ShouldCaptureMouseSelection(settings));
    }

    [Fact]
    public void UiaSelection_FromCursorPoint_ClipboardFree_ReturnsText()
    {
        var probe = UiaSelectionProvider.ClassifyUiaSelection(
            "selected text",
            fromCursorPoint: true,
            acceptCursorPointText: true);

        Assert.Equal(UiaSelectionProvider.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("selected text", probe.Text);
    }

    [Fact]
    public void UiaSelection_ClipboardFreeChromiumGesture_ReplacesWrongSameLengthBidiRun()
    {
        var probe = UiaSelectionProvider.ClassifyUiaSelection(
            "ب ثاني ",
            fromCursorPoint: false,
            gestureText: "ChatGPT",
            requireGestureText: true);

        Assert.Equal(UiaSelectionProvider.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("ChatGPT", probe.Text);
    }

    [Fact]
    public void UiaSelection_ClipboardFreeChromiumDoubleClick_RejectsDifferentLengthGuess()
    {
        var probe = UiaSelectionProvider.ClassifyUiaSelection(
            "selected text",
            fromCursorPoint: false,
            gestureText: "word",
            requireGestureText: true);

        Assert.Equal(UiaSelectionProvider.SelectionProbeOutcome.UntrustedText, probe.Outcome);
        Assert.Null(probe.Text);
    }

    [Fact]
    public void UiaSelection_ClipboardFreeChromiumDrag_AcceptsDifferentLengthMixedBidiRange()
    {
        var probe = UiaSelectionProvider.ClassifyUiaSelection(
            "كلمات عربية مجاورة",
            fromCursorPoint: false,
            gestureText: "هل تريد ChatGPT الآن؟",
            requireGestureText: true,
            acceptGestureLengthMismatch: true);

        Assert.Equal(UiaSelectionProvider.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("هل تريد ChatGPT الآن؟", probe.Text);
    }

    [Theory]
    [InlineData(1238, 1463)]
    [InlineData(1463, 1238)]
    public void ChromiumDragGeometry_IgnoresDuplicateBidiCaretRectangle(
        int startX,
        int endX)
    {
        var gesture = new UiaSelectionProvider.SelectionGesture(
            IsDrag: true,
            ClickCount: 1,
            StartX: startX,
            StartY: 1485,
            EndX: endX,
            EndY: 1485);

        Assert.False(UiaSelectionProvider.IsCharacterInsideDrag(
            [
                new System.Windows.Rect(1439, 1453, 1, 57),
                new System.Windows.Rect(2491, 1453, 32, 57),
            ],
            gesture));
        Assert.True(UiaSelectionProvider.IsCharacterInsideDrag(
            [new System.Windows.Rect(1439, 1453, 12, 57)],
            gesture));
    }

    [Fact]
    public void ChromiumDragGeometry_RotatesEnglishRunBackToLogicalOrder()
    {
        const string visualLine = "ChatGPTمرحبا ";
        var text = UiaSelectionProvider.MapVisualSelectionToLogicalText(
            visualLine,
            [
                new UiaSelectionProvider.Utf16Span(0, "ChatGPT".Length),
                new UiaSelectionProvider.Utf16Span(visualLine.Length - 1, 1),
            ],
            "earlier line\nمرحبا ChatGPT\nlater line");

        Assert.Equal("ChatGPT", text);
    }

    [Fact]
    public void ChromiumDragGeometry_ReturnsMixedSelectionInLogicalOrder()
    {
        const string visualLine = "ChatGPTمرحبا ";
        var text = UiaSelectionProvider.MapVisualSelectionToLogicalText(
            visualLine,
            [
                new UiaSelectionProvider.Utf16Span(0, "ChatGPT".Length),
                new UiaSelectionProvider.Utf16Span(visualLine.Length - 2, 1),
                new UiaSelectionProvider.Utf16Span(visualLine.Length - 1, 1),
            ],
            "مرحبا ChatGPT");

        Assert.Equal("ا ChatGPT", text);
    }

    [Fact]
    public void ChromiumDragGeometry_RejectsNoncontiguousLogicalGuess()
    {
        const string visualLine = "ChatGPTمرحبا ";
        var text = UiaSelectionProvider.MapVisualSelectionToLogicalText(
            visualLine,
            [
                new UiaSelectionProvider.Utf16Span(0, "ChatGPT".Length),
                new UiaSelectionProvider.Utf16Span("ChatGPT".Length, 1),
            ],
            "مرحبا ChatGPT");

        Assert.Null(text);
    }

    [Fact]
    public void UiaSelection_FromCursorPoint_DiscardsPossiblyWrongText()
    {
        var probe = UiaSelectionProvider.ClassifyUiaSelection(
            "نص مجاور غير محدد",
            fromCursorPoint: true);

        Assert.Equal(
            UiaSelectionProvider.SelectionProbeOutcome.ConfirmedTextPreferExact,
            probe.Outcome);
        Assert.Null(probe.Text);
    }

    [Fact]
    public void UiaSelection_FromFocusedTree_ReturnsTextDirectly()
    {
        var probe = UiaSelectionProvider.ClassifyUiaSelection(
            "selected text",
            fromCursorPoint: false,
            automationRuntimeId: "42,1");

        Assert.Equal(UiaSelectionProvider.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("selected text", probe.Text);
        Assert.Equal("42,1", probe.AutomationRuntimeId);
    }

    [Fact]
    public void UiaSelection_FromFocusedTree_WithExactCopy_DiscardsPossiblyWrongBidiText()
    {
        var probe = UiaSelectionProvider.ClassifyUiaSelection(
            "دراما العائلي الكوري",
            fromCursorPoint: false,
            preferExactCopy: true,
            automationRuntimeId: "42,1");

        Assert.Equal(
            UiaSelectionProvider.SelectionProbeOutcome.ConfirmedTextPreferExact,
            probe.Outcome);
        Assert.Null(probe.Text);
        Assert.Equal("42,1", probe.AutomationRuntimeId);
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
    public void Cursor_HardNonTextAtBothPoints_Suppresses()
    {
        // Genuine resize / wait / crosshair / no-drop drags: positively identified HARD non-text
        // cursors. Still bail before any capture. NOTE: this no longer covers arrow/hand — those
        // are AmbiguousNonText now and get a quiet capture (see below).
        Assert.Null(CursorShape.DecideCaptureAggressiveness(CursorKind.KnownNonText, CursorKind.KnownNonText));
    }

    // ── The X/Twitter-feed / App-Store fix: arrow & hand no longer hard-suppress ──

    [Fact]
    public void Cursor_AmbiguousAtBothPoints_FallsBackToQuietCapture()
    {
        // THE regression pin for this fix. Arrow/hand at press AND release used to hard-suppress
        // (return null), silently killing selections over click-to-open web text — X/Twitter feed
        // tweets, App Store descriptions styled cursor:default. Now a quiet capture: UIA reads the
        // selection, and (Quiet) no synthetic keystroke is ever injected on the arrow/hand target.
        Assert.Equal(CaptureAggressiveness.Quiet,
            CursorShape.DecideCaptureAggressiveness(CursorKind.AmbiguousNonText, CursorKind.AmbiguousNonText));
    }

    [Theory]
    [InlineData(CursorKind.KnownNonText, CursorKind.AmbiguousNonText)]
    [InlineData(CursorKind.AmbiguousNonText, CursorKind.KnownNonText)]
    public void Cursor_MixedHardAndAmbiguous_FallsBackToQuietCapture(CursorKind down, CursorKind up) =>
        // Intentional: ONLY both-hard suppresses. A resize drag that clips an arrow region falls to
        // Quiet (a silent no-op — no text is selected) rather than widening suppression back onto
        // the arrow/hand family this fix rescues.
        Assert.Equal(CaptureAggressiveness.Quiet, CursorShape.DecideCaptureAggressiveness(down, up));

    [Theory]
    [InlineData(CursorKind.TextIBeam, CursorKind.AmbiguousNonText)]
    [InlineData(CursorKind.AmbiguousNonText, CursorKind.TextIBeam)]
    [InlineData(CursorKind.Unreadable, CursorKind.AmbiguousNonText)]
    [InlineData(CursorKind.AmbiguousNonText, CursorKind.Unreadable)]
    public void Cursor_IBeamOrUnreadableBeatsAmbiguous(CursorKind down, CursorKind up) =>
        // I-beam / Unreadable still short-circuit ahead of any ambiguous logic — the opened-tweet
        // path (I-beam → Full, keystroke fallback available) is untouched.
        Assert.Equal(CaptureAggressiveness.Full, CursorShape.DecideCaptureAggressiveness(down, up));

    [Fact]
    public void IsAmbiguousBothPoints_TrueOnlyForArrowHandAtBothEnds()
    {
        Assert.True(CursorShape.IsAmbiguousBothPoints(CursorKind.AmbiguousNonText, CursorKind.AmbiguousNonText));
        Assert.False(CursorShape.IsAmbiguousBothPoints(CursorKind.AmbiguousNonText, CursorKind.Unknown));
        Assert.False(CursorShape.IsAmbiguousBothPoints(CursorKind.Unknown, CursorKind.Unknown));       // custom cursor
        Assert.False(CursorShape.IsAmbiguousBothPoints(CursorKind.TextIBeam, CursorKind.AmbiguousNonText));
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
        var ibeam = LoadCursor(IntPtr.Zero, 32513);  // IDC_IBEAM
        var arrow = LoadCursor(IntPtr.Zero, 32512);  // IDC_ARROW  — Ambiguous (sits over selectable web text)
        var hand = LoadCursor(IntPtr.Zero, 32649);   // IDC_HAND   — Ambiguous (click-to-open web text)
        var sizeWE = LoadCursor(IntPtr.Zero, 32644); // IDC_SIZEWE — hard non-text (resize)
        var wait = LoadCursor(IntPtr.Zero, 32514);   // IDC_WAIT   — hard non-text (busy)
        Assert.Equal(CursorKind.TextIBeam, CursorShape.ClassifyHandle(ibeam));
        Assert.Equal(CursorKind.AmbiguousNonText, CursorShape.ClassifyHandle(arrow));
        Assert.Equal(CursorKind.AmbiguousNonText, CursorShape.ClassifyHandle(hand));
        Assert.Equal(CursorKind.KnownNonText, CursorShape.ClassifyHandle(sizeWE));
        Assert.Equal(CursorKind.KnownNonText, CursorShape.ClassifyHandle(wait));
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
