using System.Runtime.InteropServices;
using SnapActions.Config;
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
    public void Plan_ClipboardFree_NeverUsesCopyLayers()
    {
        foreach (var outcome in Enum.GetValues<TextCapture.SelectionProbeOutcome>())
        foreach (var isDrag in new[] { false, true })
        foreach (var allowSyntheticKeys in new[] { false, true })
        foreach (var ambiguousCursor in new[] { false, true })
        {
            var plan = TextCapture.DecidePlan(
                outcome,
                isDrag,
                allowSyntheticKeys,
                ambiguousCursor,
                allowClipboardCapture: false);

            Assert.False(plan.RunWmCopy);
            Assert.False(plan.RunKeystroke);
        }
    }

    [Fact]
    public void Plan_ClipboardFree_UnknownRetainsUiaFallback()
    {
        var plan = TextCapture.DecidePlan(
            TextCapture.SelectionProbeOutcome.Unknown,
            isDrag: true,
            allowSyntheticKeys: true,
            ambiguousCursor: false,
            allowClipboardCapture: false);

        Assert.Equal(new TextCapture.CapturePlan(false, true, false), plan);
    }

    [Fact]
    public void UiaSelection_FromCursorPoint_ClipboardFree_ReturnsText()
    {
        var probe = TextCapture.ClassifyUiaSelection(
            "selected text",
            fromCursorPoint: true,
            acceptCursorPointText: true);

        Assert.Equal(TextCapture.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("selected text", probe.Text);
    }

    [Fact]
    public void UiaSelection_ClipboardFreeChromiumGesture_ReplacesWrongSameLengthBidiRun()
    {
        var probe = TextCapture.ClassifyUiaSelection(
            "ب ثاني ",
            fromCursorPoint: false,
            gestureText: "ChatGPT",
            requireGestureText: true);

        Assert.Equal(TextCapture.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("ChatGPT", probe.Text);
    }

    [Fact]
    public void UiaSelection_ClipboardFreeChromiumDoubleClick_RejectsDifferentLengthGuess()
    {
        var probe = TextCapture.ClassifyUiaSelection(
            "selected text",
            fromCursorPoint: false,
            gestureText: "word",
            requireGestureText: true);

        Assert.Equal(TextCapture.SelectionProbeOutcome.UntrustedText, probe.Outcome);
        Assert.Null(probe.Text);
    }

    [Fact]
    public void UiaSelection_ClipboardFreeChromiumDrag_AcceptsDifferentLengthMixedBidiRange()
    {
        var probe = TextCapture.ClassifyUiaSelection(
            "كلمات عربية مجاورة",
            fromCursorPoint: false,
            gestureText: "هل تريد ChatGPT الآن؟",
            requireGestureText: true,
            acceptGestureLengthMismatch: true);

        Assert.Equal(TextCapture.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("هل تريد ChatGPT الآن؟", probe.Text);
    }

    [Theory]
    [InlineData(1238, 1463)]
    [InlineData(1463, 1238)]
    public void ChromiumDragGeometry_IgnoresDuplicateBidiCaretRectangle(
        int startX,
        int endX)
    {
        var gesture = new TextCapture.SelectionGesture(
            IsDrag: true,
            ClickCount: 1,
            StartX: startX,
            StartY: 1485,
            EndX: endX,
            EndY: 1485);

        Assert.False(TextCapture.IsCharacterInsideDrag(
            [
                new System.Windows.Rect(1439, 1453, 1, 57),
                new System.Windows.Rect(2491, 1453, 32, 57),
            ],
            gesture));
        Assert.True(TextCapture.IsCharacterInsideDrag(
            [new System.Windows.Rect(1439, 1453, 12, 57)],
            gesture));
    }

    [Fact]
    public void ChromiumDragGeometry_RotatesEnglishRunBackToLogicalOrder()
    {
        const string visualLine = "ChatGPTمرحبا ";
        var text = TextCapture.MapVisualSelectionToLogicalText(
            visualLine,
            [
                new TextCapture.Utf16Span(0, "ChatGPT".Length),
                new TextCapture.Utf16Span(visualLine.Length - 1, 1),
            ],
            "earlier line\nمرحبا ChatGPT\nlater line");

        Assert.Equal("ChatGPT", text);
    }

    [Fact]
    public void ChromiumDragGeometry_ReturnsMixedSelectionInLogicalOrder()
    {
        const string visualLine = "ChatGPTمرحبا ";
        var text = TextCapture.MapVisualSelectionToLogicalText(
            visualLine,
            [
                new TextCapture.Utf16Span(0, "ChatGPT".Length),
                new TextCapture.Utf16Span(visualLine.Length - 2, 1),
                new TextCapture.Utf16Span(visualLine.Length - 1, 1),
            ],
            "مرحبا ChatGPT");

        Assert.Equal("ا ChatGPT", text);
    }

    [Fact]
    public void ChromiumDragGeometry_RejectsNoncontiguousLogicalGuess()
    {
        const string visualLine = "ChatGPTمرحبا ";
        var text = TextCapture.MapVisualSelectionToLogicalText(
            visualLine,
            [
                new TextCapture.Utf16Span(0, "ChatGPT".Length),
                new TextCapture.Utf16Span("ChatGPT".Length, 1),
            ],
            "مرحبا ChatGPT");

        Assert.Null(text);
    }

    [Fact]
    public void Plan_ClipboardFree_UntrustedText_FailsClosed()
    {
        var plan = TextCapture.DecidePlan(
            TextCapture.SelectionProbeOutcome.UntrustedText,
            isDrag: true,
            allowSyntheticKeys: false,
            allowClipboardCapture: false);

        Assert.Equal(new TextCapture.CapturePlan(false, false, false), plan);
    }

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
                 {
                     TextCapture.SelectionProbeOutcome.ConfirmedTextPreferExact,
                     TextCapture.SelectionProbeOutcome.EmptyTextPattern,
                     TextCapture.SelectionProbeOutcome.Unknown
                 })
        {
            Assert.False(TextCapture.DecidePlan(outcome, isDrag: true, allowSyntheticKeys: false).RunKeystroke);
            Assert.False(TextCapture.DecidePlan(outcome, isDrag: false, allowSyntheticKeys: false).RunKeystroke);
        }
    }

    [Fact]
    public void Plan_Unknown_WithExactCopy_SkipsUia()
    {
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.Unknown,
            isDrag: false, allowSyntheticKeys: true);
        Assert.Equal(new TextCapture.CapturePlan(RunWmCopy: true, RunUia: false, RunKeystroke: true), plan);
    }

    [Fact]
    public void Plan_AmbiguousMultiClick_Unknown_RunsNothing()
    {
        // Arrow/hand + multi-click (NOT a drag) + no keystroke: UIA can't confirm text, so withhold
        // everything — a double-click is the ambiguous gesture we stay cautious on, and a WM_COPY on
        // a text-bearing item could pop a spurious toolbar.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.Unknown,
            isDrag: false, allowSyntheticKeys: false, ambiguousCursor: true);
        Assert.Equal(new TextCapture.CapturePlan(false, false, false), plan);
    }

    [Fact]
    public void Plan_AmbiguousDrag_Unknown_RunsKeystrokeCascade()
    {
        // THE X/Twitter feed fix: an arrow/hand DRAG (strong selection signal) whose text UIA can't
        // see runs the exact clipboard cascade including Ctrl+Insert (the caller sets keys=true
        // for it). A second UIA read must not preempt that exact copy with an adjacent bidi run.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.Unknown,
            isDrag: true, allowSyntheticKeys: true, ambiguousCursor: true);
        Assert.Equal(new TextCapture.CapturePlan(RunWmCopy: true, RunUia: false, RunKeystroke: true), plan);
    }

    [Fact]
    public void Plan_AmbiguousDrag_ItemSuppress_IsOverriddenToKeystrokeCascade()
    {
        // A feed tweet is a ListItem+SelectionItemPattern that HOLDS selectable text; an ambiguous
        // drag over it must not hard-stop on the item signal — run the exact clipboard cascade.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.SuppressItemElement,
            isDrag: true, allowSyntheticKeys: true, ambiguousCursor: true);
        Assert.Equal(new TextCapture.CapturePlan(true, false, true), plan);
    }

    [Fact]
    public void Plan_ItemSuppress_NonAmbiguous_StillRunsNothing()
    {
        // A genuine Explorer/desktop item (not an ambiguous drag) still hard-stops before any capture.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.SuppressItemElement,
            isDrag: true, allowSyntheticKeys: true, ambiguousCursor: false);
        Assert.Equal(new TextCapture.CapturePlan(false, false, false), plan);
    }

    [Fact]
    public void Plan_AmbiguousDrag_ItemSuppress_ShellGated_RunsNothing()
    {
        // Explorer / file-manager exclusion: the caller clears allowSyntheticKeys there, which
        // disables the item-suppress override — so a file row still hard-stops and no Ctrl+Insert
        // fires to copy files or downgrade a pending Ctrl+X cut. (ambiguous drag, but keys=false.)
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.SuppressItemElement,
            isDrag: true, allowSyntheticKeys: false, ambiguousCursor: true);
        Assert.Equal(new TextCapture.CapturePlan(false, false, false), plan);
    }

    [Fact]
    public void Plan_ConfirmedTextPreferExact_UsesClipboardCopyNotUia()
    {
        // ChatGPT/Twitter can expose selectable message/feed text inside a focused ListItem.
        // UIA proves that a selection exists, but the app's copy path must supply the exact text.
        var plan = TextCapture.DecidePlan(
            TextCapture.SelectionProbeOutcome.ConfirmedTextPreferExact,
            isDrag: true,
            allowSyntheticKeys: true);
        Assert.Equal(
            new TextCapture.CapturePlan(
                RunWmCopy: true, RunUia: false, RunKeystroke: true),
            plan);
    }

    [Fact]
    public void Plan_ConfirmedTextPreferExact_WithoutSyntheticKeys_UsesWmCopyOnly()
    {
        var plan = TextCapture.DecidePlan(
            TextCapture.SelectionProbeOutcome.ConfirmedTextPreferExact,
            isDrag: true,
            allowSyntheticKeys: false);
        Assert.Equal(
            new TextCapture.CapturePlan(
                RunWmCopy: true, RunUia: false, RunKeystroke: false),
            plan);
    }

    [Fact]
    public void UiaSelection_FromCursorPoint_DiscardsPossiblyWrongText()
    {
        var probe = TextCapture.ClassifyUiaSelection(
            "نص مجاور غير محدد",
            fromCursorPoint: true);

        Assert.Equal(
            TextCapture.SelectionProbeOutcome.ConfirmedTextPreferExact,
            probe.Outcome);
        Assert.Null(probe.Text);
    }

    [Fact]
    public void UiaSelection_FromFocusedTree_ReturnsTextDirectly()
    {
        var probe = TextCapture.ClassifyUiaSelection(
            "selected text",
            fromCursorPoint: false,
            automationRuntimeId: "42,1");

        Assert.Equal(TextCapture.SelectionProbeOutcome.HasText, probe.Outcome);
        Assert.Equal("selected text", probe.Text);
        Assert.Equal("42,1", probe.AutomationRuntimeId);
    }

    [Fact]
    public void UiaSelection_FromFocusedTree_WithExactCopy_DiscardsPossiblyWrongBidiText()
    {
        var probe = TextCapture.ClassifyUiaSelection(
            "دراما العائلي الكوري",
            fromCursorPoint: false,
            preferExactCopy: true,
            automationRuntimeId: "42,1");

        Assert.Equal(
            TextCapture.SelectionProbeOutcome.ConfirmedTextPreferExact,
            probe.Outcome);
        Assert.Null(probe.Text);
        Assert.Equal("42,1", probe.AutomationRuntimeId);
    }

    [Fact]
    public void Plan_AmbiguousCursor_EmptyTextPattern_KeepsWmCopy()
    {
        // The feed's lying-provider path must survive: arrow/hand + EmptyTextPattern keeps WM_COPY
        // (never a keystroke under a quiet/ambiguous capture). Only the Unknown outcome is withheld.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.EmptyTextPattern,
            isDrag: true, allowSyntheticKeys: false, ambiguousCursor: true);
        Assert.Equal(new TextCapture.CapturePlan(RunWmCopy: true, RunUia: false, RunKeystroke: false), plan);
    }

    [Fact]
    public void Plan_NonAmbiguousCursor_Unknown_KeepsWmCopyFallback()
    {
        // A custom-cursor quiet capture (Unknown cursor KIND, not ambiguous) keeps the WM_COPY
        // fallback — custom-I-beam editors/terminals that expose no UIA TextPattern rely on it.
        var plan = TextCapture.DecidePlan(TextCapture.SelectionProbeOutcome.Unknown,
            isDrag: true, allowSyntheticKeys: false, ambiguousCursor: false);
        Assert.Equal(new TextCapture.CapturePlan(RunWmCopy: true, RunUia: true, RunKeystroke: false), plan);
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
