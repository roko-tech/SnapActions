using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace SnapActions.Core;

internal static class UiaSelectionProvider
{
    internal readonly record struct CaptureResult(
        string? Text,
        SelectionOperation Operation);

    internal readonly record struct SelectionGesture(
        bool IsDrag,
        int ClickCount,
        int StartX,
        int StartY,
        int EndX,
        int EndY);

    internal readonly record struct Utf16Span(int Start, int Length)
    {
        internal int End => Start + Length;
    }

    private static readonly SemaphoreSlim CaptureLock = new(1, 1);

    /// <summary>Reads through UI Automation only. Capturing can never synthesize copy or touch the clipboard.</summary>
    internal static async Task<CaptureResult> CaptureSelectedTextAsync(
        SelectionOperation operation, SelectionGesture gesture, int cursorX, int cursorY)
    {
        CaptureResult Result(string? text) => new(
            text?.Length <= BrowserMessage.MaximumTextLength ? text : null, operation);
        await CaptureLock.WaitAsync();
        try
        {
            if (!await operation.CanInjectInputAsync()) return Result(null);
            if (UiaSkipApps.Contains(ForegroundApp.GetActiveProcessName() ?? "")) return Result(null);
            var probe = await RunBoundedUiaAsync(
                () => ProbeSelectionViaUIA(cursorX, cursorY, operation.Target.ProcessId,
                    operation.Target.AutomationRuntimeId, gesture, preferExactCopy: false, acceptCursorPointText: true),
                new SelectionProbe(SelectionProbeOutcome.Unknown, null, "UIA busy or unavailable"),
                busyHandoffMs: operation.Target.AutomationRuntimeId == null ? UiaBusyHandoffMs : 0);
            if (!await operation.CanInjectInputAsync()) return Result(null);
            operation = operation.WithTarget(BindProbeIdentity(operation.Target, probe));
            if (probe.Outcome == SelectionProbeOutcome.HasText)
            {
                operation = operation.WithInputValidation(probe.ValidateInput);
                return Result(probe.Text);
            }
            if (probe.Outcome is SelectionProbeOutcome.SuppressItemElement or SelectionProbeOutcome.UntrustedText)
                return Result(null);
            var fallback = await RunBoundedUiaAsync(
                () => CopyViaUIA(operation.Target.ProcessId, operation.Target.AutomationRuntimeId), null);
            if (fallback is { } selected)
                operation = operation.WithTarget(BindProbeIdentity(operation.Target, selected))
                    .WithInputValidation(selected.ValidateInput);
            return Result(await operation.CanInjectInputAsync() ? fallback?.Text : null);
        }
        catch (Exception ex)
        {
            SnapActions.Helpers.Log.Error("UIA capture", ex);
            return Result(null);
        }
        finally { CaptureLock.Release(); }
    }

    internal static async Task<SelectionOperation> BindInputSelectionAsync(SelectionOperation operation)
    {
        if (!await operation.CanInjectInputAsync()) return operation.WithInputValidation(null);
        var probe = await RunBoundedUiaAsync(
            () => CopyViaUIA(operation.Target.ProcessId, operation.Target.AutomationRuntimeId, allowEmpty: true), null);
        return probe is { } selected
            ? operation.WithTarget(BindProbeIdentity(operation.Target, selected)).WithInputValidation(selected.ValidateInput)
            : operation.WithInputValidation(null);
    }

    private static Func<bool>? CreateInputValidation(TextPattern pattern, TextPatternRange[] ranges, string expectedText)
    {
        try
        {
            if (ranges.Length is 0 or > 256 || expectedText.Length > BrowserMessage.MaximumTextLength) return null;
            var captured = ranges.Select(range => range.Clone()).ToArray();
            var text = captured.Select(range => range.GetText(BrowserMessage.MaximumTextLength + 1)).ToArray();
            // Geometry may rescue Chromium display text even when UIA reports an adjacent range.
            // Such a capture remains useful for Copy but cannot authorize an edit of that range.
            if (CombineSelectionRanges(text) != expectedText) return null;
            return () =>
            {
                if (!ForegroundApp.IsEditableFieldFocused()) return false;
                var current = pattern.GetSelection();
                if (current.Length != captured.Length) return false;
                for (int i = 0; i < captured.Length; i++)
                    if (captured[i].CompareEndpoints(TextPatternRangeEndpoint.Start, current[i], TextPatternRangeEndpoint.Start) != 0
                        || captured[i].CompareEndpoints(TextPatternRangeEndpoint.End, current[i], TextPatternRangeEndpoint.End) != 0
                        || current[i].GetText(BrowserMessage.MaximumTextLength + 1) != text[i])
                        return false;
                return true;
            };
        }
        catch { return null; }
    }

    private static readonly HashSet<string> UiaSkipApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "thunderbird",
    };

    private const int UiaCallTimeoutMs = 500;
    private const int UiaBusyHandoffMs = 50;

    /// <summary>
    /// Runs a UIA call with a hard timeout and a shared pre-start single-flight gate. If a broken
    /// provider blocks inside GetSelection/GetText, the await returns its fallback but the gate
    /// stays occupied until that underlying call really exits. Calls normally fail fast while it
    /// is occupied; the selection pre-gate may wait once for a short event-identity handoff, then
    /// retry without ever admitting concurrent UIA workers.
    /// </summary>
    private static Task<T> RunBoundedUiaAsync<T>(
        Func<T> uiaCall,
        T onTimeout,
        int busyHandoffMs = 0) =>
        ForegroundGuard.RunBoundedAutomationAsync(
            uiaCall, onTimeout, UiaCallTimeoutMs, busyHandoffMs);
    /// <summary>
    /// Maximum UIA parent levels to walk when probing for a TextPattern. Same rationale as
    /// <see cref="ForegroundApp.IsTextInputAtPoint"/>: leaf elements (a span / anchor / svg)
    /// usually don't expose TextPattern themselves even though the paragraph / document
    /// ancestor does.
    /// </summary>
    private const int TextPatternParentWalkDepth = 6;

    internal enum SelectionProbeOutcome
    {
        /// <summary>UIA returned selected text that passed identity and geometry checks.</summary>
        HasText,
        /// <summary>UIA confirmed a selection, but its returned text is not trusted.
        /// A later explicit user copy can still provide text.</summary>
        ConfirmedTextPreferExact,
        /// <summary>UIA returned text, but an exact clipboard-free gesture reconstruction was
        /// unavailable or a double-click word did not match its selection length.</summary>
        UntrustedText,
        /// <summary>The focused element is a non-text item (Explorer file, desktop icon, list row).
        /// Definitive — capture must not run (WM_COPY would copy the item's name).</summary>
        SuppressItemElement,
        /// <summary>A TextPattern was found but reported an empty selection. Usually means "no
        /// selection", but some providers lie (report empty despite a real selection), so this is
        /// a signal to try the remaining read-only UIA path.</summary>
        EmptyTextPattern,
        /// <summary>UIA could not establish a text selection.</summary>
        Unknown,
    }

    internal readonly record struct SelectionProbe(
        SelectionProbeOutcome Outcome,
        string? Text,
        string? Reason,
        string? AutomationRuntimeId = null,
        Func<bool>? ValidateInput = null);

    internal static SelectionProbe ClassifyUiaSelection(
        string text,
        bool fromCursorPoint,
        bool preferExactCopy = false,
        bool acceptCursorPointText = false,
        string? automationRuntimeId = null,
        string? gestureText = null,
        bool requireGestureText = false,
        bool acceptGestureLengthMismatch = false)
    {
        if (text.Length > BrowserMessage.MaximumTextLength || gestureText?.Length > BrowserMessage.MaximumTextLength)
            return new SelectionProbe(SelectionProbeOutcome.UntrustedText, null, "Selection is too large", automationRuntimeId);
        if ((fromCursorPoint && !acceptCursorPointText) || preferExactCopy)
        {
            return new SelectionProbe(
                SelectionProbeOutcome.ConfirmedTextPreferExact,
                null,
                "selection confirmed; exact copy preferred",
                automationRuntimeId);
        }

        bool gestureDefinesSelection = !string.IsNullOrEmpty(gestureText)
                                       && (acceptGestureLengthMismatch
                                           || gestureText.Length == text.Length);
        if (requireGestureText && !gestureDefinesSelection)
        {
            return new SelectionProbe(
                SelectionProbeOutcome.UntrustedText,
                null,
                string.IsNullOrEmpty(gestureText)
                    ? "Chromium gesture range was unavailable"
                    : "Chromium double-click range did not match the selected range length",
                automationRuntimeId);
        }

        return new SelectionProbe(
            SelectionProbeOutcome.HasText,
            gestureDefinesSelection ? gestureText : text,
            gestureDefinesSelection
                ? "gesture-derived selection text accepted"
                : "UIA selection text accepted",
            automationRuntimeId);
    }

    internal static ForegroundTarget BindProbeIdentity(
        ForegroundTarget target,
        SelectionProbe probe) =>
        target.IsComplete
        && target.AutomationRuntimeId == null
        && probe.AutomationRuntimeId != null
            ? target with { AutomationRuntimeId = probe.AutomationRuntimeId }
            : target;

    /// <summary>
    /// Item-style control types that are NOT text. When the focused element is one of these
    /// AND exposes SelectionItemPattern AND we found no TextPattern up the tree, we treat the
    /// "selection" as an item selection (file in Explorer, desktop icon, list-box row, tree
    /// node) and suppress. Deliberately narrow — Pane / Custom / Document stay out because
    /// browsers and Electron focus those for real text contexts.
    /// </summary>
    private static readonly System.Windows.Automation.ControlType[] NonTextItemTypes =
    [
        System.Windows.Automation.ControlType.DataItem,
        System.Windows.Automation.ControlType.ListItem,
        System.Windows.Automation.ControlType.TreeItem,
    ];

    /// <summary>Reads selected text from the original focused element or gesture point.
    /// Chromium gestures require matching geometry to avoid adjacent bidi runs. Missing or
    /// ambiguous evidence stays unavailable; automatic capture never invokes a copy fallback.</summary>
    internal static SelectionProbe ProbeSelectionViaUIA(
        int cursorX,
        int cursorY,
        uint expectedProcessId,
        string? expectedRuntimeId,
        SelectionGesture gesture,
        bool preferExactCopy,
        bool acceptCursorPointText = false)
    {
        AutomationElement? originalFocused = null;
        try
        {
            originalFocused = AutomationElement.FocusedElement;
            if (originalFocused == null)
                return new SelectionProbe(SelectionProbeOutcome.Unknown, null, "no focused element");
            if ((uint)originalFocused.Current.ProcessId != expectedProcessId)
                return new SelectionProbe(
                    SelectionProbeOutcome.Unknown, null, "focused element belongs to another process");
            if (!MatchesAutomationRuntimeId(
                    originalFocused, expectedRuntimeId))
                return new SelectionProbe(
                    SelectionProbeOutcome.Unknown, null, "focused element identity changed");
            string? RuntimeIdForResult() =>
                expectedRuntimeId
                ?? TryReadAutomationRuntimeId(originalFocused);

            // Walk up looking for TextPattern. If ANY ancestor has a non-empty selection,
            // return that focused-tree text immediately. If we exhaust the walk and saw at
            // least one TextPattern but all were empty → restrict the fallback. If we never
            // saw TextPattern → fall through to the item-element check below.
            var walker = TreeWalker.RawViewWalker;
            var element = originalFocused;
            bool sawAnyTextPattern = false;
            for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
            {
                try
                {
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                    {
                        sawAnyTextPattern = true;
                        var tp = (TextPattern)pat;
                        var ranges = tp.GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            var combined = CombineSelectionRanges(ranges.Select(r => r.GetText(BrowserMessage.MaximumTextLength + 1)));
                            if (!string.IsNullOrEmpty(combined))
                            {
                                bool requireGestureText = acceptCursorPointText
                                    && RequiresChromiumGestureText(element, gesture);
                                var gestureText = requireGestureText
                                    ? TryReadChromiumSelectionFromGesture(
                                        tp, element, gesture, combined.Length)
                                    : null;
                                var selected = ClassifyUiaSelection(
                                    combined,
                                    fromCursorPoint: false,
                                    preferExactCopy: preferExactCopy,
                                    automationRuntimeId: RuntimeIdForResult(),
                                    gestureText: gestureText,
                                    requireGestureText: requireGestureText,
                                    acceptGestureLengthMismatch: gesture.IsDrag);
                                return selected with { ValidateInput = selected.Outcome == SelectionProbeOutcome.HasText
                                    ? CreateInputValidation(tp, ranges, selected.Text!) : null };
                            }
                        }
                        // TextPattern at this level returned no selection text. Keep walking up
                        // — an ancestor pane / document may have the real selection (browsers
                        // often expose TextPattern at multiple levels with the leaf empty).
                    }
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }

            if (sawAnyTextPattern)
                return new SelectionProbe(SelectionProbeOutcome.EmptyTextPattern,
                    null,
                    "TextPattern present but selection is empty",
                    RuntimeIdForResult());

            // No TextPattern anywhere up the walk from FOCUS. Before classifying, read the
            // selection from the element UNDER THE CURSOR: X/Twitter focuses the tweet container
            // (a ListItem — or, inconsistently, a plain group), not the text, so the upward walk
            // from focus misses the tweet's own text, which sits right under the cursor. Covers
            // both the item case AND the plain-Unknown case.
            // Read the selection from the element UNDER THE CURSOR. For bidi content the returned
            // string may be an adjacent run rather than the exact visual selection, but a non-empty
            // range still proves this is selectable text rather than a bare file/list item.
            var atPoint = TryReadSelectionAtPoint(
                cursorX, cursorY, expectedProcessId, gesture, acceptCursorPointText);
            if (atPoint is { } pointSelection)
            {
                var selected = ClassifyUiaSelection(
                    pointSelection.Text,
                    fromCursorPoint: true,
                    acceptCursorPointText: acceptCursorPointText,
                    automationRuntimeId: RuntimeIdForResult(),
                    gestureText: pointSelection.GestureText,
                    requireGestureText: pointSelection.RequireGestureText,
                    acceptGestureLengthMismatch: gesture.IsDrag);
                return selected with { ValidateInput = selected.Outcome == SelectionProbeOutcome.HasText
                    ? pointSelection.ValidateInput : null };
            }

            // Layer C: check the originally-focused element for non-text item patterns —
            // Explorer file rows, desktop icons, list-box rows. SelectionItemPattern means
            // "I am a selectable item" (vs. text); ControlType keeps us off Pane / Custom /
            // Document which browsers and Electron focus for real text contexts.
            try
            {
                var ct = originalFocused.Current.ControlType;
                if (NonTextItemTypes.Contains(ct)
                    && originalFocused.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _))
                    return new SelectionProbe(SelectionProbeOutcome.SuppressItemElement,
                        null,
                        $"focused element is {ct.ProgrammaticName} with SelectionItemPattern",
                        RuntimeIdForResult());
            }
            catch { /* couldn't read ControlType — fall through to Unknown */ }

            return new SelectionProbe(
                SelectionProbeOutcome.Unknown,
                null,
                "no TextPattern, not a known non-text item",
                RuntimeIdForResult());
        }
        catch (Exception ex)
        {
            // A provider failure may try the remaining read-only UIA path, never a copy command.
            return new SelectionProbe(SelectionProbeOutcome.Unknown, null, $"UIA exception: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Rebuilds a Chromium selection from the mouse coordinates instead of trusting
    /// TextPattern.GetSelection().GetText(), which can return an adjacent run for mixed RTL/LTR
    /// content. Same-line drags select the characters whose visual centers fall inside the drag;
    /// the visual line is then rotated back to the logical order exposed by the element name.
    /// Double-click word expansion is accepted only when its UTF-16 length matches GetSelection.
    /// </summary>
    private static bool RequiresChromiumGestureText(
        AutomationElement element,
        SelectionGesture gesture)
    {
        if (!gesture.IsDrag && gesture.ClickCount != 2) return false;
        try
        {
            return string.Equals(
                element.Current.FrameworkId,
                "Chrome",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryReadChromiumSelectionFromGesture(
        TextPattern textPattern,
        AutomationElement element,
        SelectionGesture gesture,
        int selectedLength)
    {
        try
        {
            if (gesture.IsDrag)
                return TryReadChromiumDragFromGeometry(textPattern, element, gesture);
            if (gesture.ClickCount != 2) return null;

            var range = textPattern.RangeFromPoint(
                new Point(gesture.EndX, gesture.EndY));
            range.ExpandToEnclosingUnit(TextUnit.Word);
            if (!IsRangeWithinDocument(range, textPattern.DocumentRange)) return null;

            var text = range.GetText(BrowserMessage.MaximumTextLength + 1);
            if (text.Length > selectedLength)
            {
                var withoutTrailingWhitespace = text.TrimEnd();
                if (withoutTrailingWhitespace.Length == selectedLength)
                    return withoutTrailingWhitespace;
            }

            return text;
        }
        catch
        {
            return null;
        }
    }

    private const int ChromiumGeometryLineLimit = 512;

    private static bool IsRangeWithinDocument(TextPatternRange range, TextPatternRange document) =>
        range.CompareEndpoints(TextPatternRangeEndpoint.Start, document, TextPatternRangeEndpoint.Start) >= 0
        && range.CompareEndpoints(TextPatternRangeEndpoint.End, document, TextPatternRangeEndpoint.End) <= 0;

    private static string? TryReadChromiumDragFromGeometry(
        TextPattern textPattern,
        AutomationElement element,
        SelectionGesture gesture)
    {
        var anchorLine = textPattern.RangeFromPoint(
            new Point(gesture.StartX, gesture.StartY));
        anchorLine.ExpandToEnclosingUnit(TextUnit.Line);
        var focusLine = textPattern.RangeFromPoint(
            new Point(gesture.EndX, gesture.EndY));
        focusLine.ExpandToEnclosingUnit(TextUnit.Line);

        // Chromium can return unrelated UI chrome from RangeFromPoint (observed in VS Code).
        // Matching coordinates and line identity cannot make an out-of-document range valid.
        var document = textPattern.DocumentRange;
        if (!IsRangeWithinDocument(anchorLine, document) || !IsRangeWithinDocument(focusLine, document))
            return null;

        // Cross-line selection needs caret ordering rather than a horizontal hit test. Chromium's
        // mixed-bidi caret affinity is exactly the value that proved unreliable, so fail closed.
        if (!anchorLine.Compare(focusLine)) return null;
        if (anchorLine.GetText(ChromiumGeometryLineLimit + 1).Length
            > ChromiumGeometryLineLimit)
            return null;

        var cursor = anchorLine.Clone();
        cursor.MoveEndpointByRange(
            TextPatternRangeEndpoint.End,
            cursor,
            TextPatternRangeEndpoint.Start);

        var visualText = new System.Text.StringBuilder();
        var selectedVisualText = new System.Text.StringBuilder();
        var selectedSpans = new List<Utf16Span>();
        for (int unit = 0; unit <= ChromiumGeometryLineLimit; unit++)
        {
            if (cursor.CompareEndpoints(
                    TextPatternRangeEndpoint.Start,
                    anchorLine,
                    TextPatternRangeEndpoint.End) >= 0)
                break;

            var character = cursor.Clone();
            if (character.MoveEndpointByUnit(
                    TextPatternRangeEndpoint.End,
                    TextUnit.Character,
                    1) <= 0)
                return null;
            if (character.CompareEndpoints(
                    TextPatternRangeEndpoint.End,
                    anchorLine,
                    TextPatternRangeEndpoint.End) > 0)
            {
                character.MoveEndpointByRange(
                    TextPatternRangeEndpoint.End,
                    anchorLine,
                    TextPatternRangeEndpoint.End);
            }

            var characterText = character.GetText(BrowserMessage.MaximumTextLength + 1);
            if (characterText.Length == 0) return null;
            int characterStart = visualText.Length;
            visualText.Append(characterText);

            if (IsCharacterInsideDrag(
                    character.GetBoundingRectangles(), gesture))
            {
                selectedSpans.Add(new Utf16Span(
                    characterStart, characterText.Length));
                selectedVisualText.Append(characterText);
            }

            cursor.MoveEndpointByRange(
                TextPatternRangeEndpoint.Start,
                character,
                TextPatternRangeEndpoint.End);
            cursor.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                cursor,
                TextPatternRangeEndpoint.Start);
        }

        if (cursor.CompareEndpoints(
                TextPatternRangeEndpoint.Start,
                anchorLine,
                TextPatternRangeEndpoint.End) < 0)
            return null;
        if (selectedSpans.Count == 0) return null;
        string automationName;
        try { automationName = element.Current.Name; }
        catch { automationName = string.Empty; }

        var logicalText = MapVisualSelectionToLogicalText(
            visualText.ToString(), selectedSpans, automationName);
        if (!string.IsNullOrWhiteSpace(logicalText)) return logicalText;

        // A single directional run keeps the same character order even if the provider moved the
        // run to the other side of an RTL line. Do not guess when both Arabic and Latin survived.
        var visualSelection = selectedVisualText.ToString();
        return !string.IsNullOrWhiteSpace(visualSelection)
               && !ContainsArabicAndLatin(visualSelection)
            ? visualSelection
            : null;
    }

    internal static bool IsCharacterInsideDrag(
        IReadOnlyList<Rect> rectangles,
        SelectionGesture gesture)
    {
        double minimumX = Math.Min(gesture.StartX, gesture.EndX);
        double maximumX = Math.Max(gesture.StartX, gesture.EndX);
        double minimumY = Math.Min(gesture.StartY, gesture.EndY);
        double maximumY = Math.Max(gesture.StartY, gesture.EndY);
        bool hasNonCaretRectangle = rectangles.Any(
            rectangle => rectangle.Width > 1.0 && rectangle.Height > 0);

        foreach (var rectangle in rectangles)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;
            // Chromium can attach a 1-pixel caret-affinity rectangle at a bidi boundary to a
            // character whose real glyph is at the far side of the line. Ignore only that tiny
            // duplicate; a genuinely narrow character with no wider rectangle remains eligible.
            if (hasNonCaretRectangle && rectangle.Width <= 1.0) continue;
            if (rectangle.Bottom < minimumY || rectangle.Top > maximumY) continue;
            double centerX = rectangle.Left + rectangle.Width / 2.0;
            if (centerX >= minimumX && centerX <= maximumX) return true;
        }

        return false;
    }

    internal static string? MapVisualSelectionToLogicalText(
        string visualLine,
        IReadOnlyList<Utf16Span> selectedSpans,
        string automationName)
    {
        if (visualLine.Length == 0 || selectedSpans.Count == 0) return null;
        if (selectedSpans.Any(span =>
                span.Start < 0 || span.Length <= 0 || span.End > visualLine.Length))
            return null;

        var results = new HashSet<string>(StringComparer.Ordinal);
        var logicalLines = automationName
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        foreach (var logicalLine in logicalLines)
        {
            if (logicalLine.Length != visualLine.Length) continue;
            for (int rotation = 0; rotation < visualLine.Length; rotation++)
            {
                if (!IsRotation(visualLine, logicalLine, rotation)) continue;
                var mapped = selectedSpans
                    .Select(span => new Utf16Span(
                        (span.Start - rotation + visualLine.Length)
                        % visualLine.Length,
                        span.Length))
                    .OrderBy(span => span.Start)
                    .ToArray();
                if (mapped.Any(span => span.End > logicalLine.Length)) continue;

                int start = mapped[0].Start;
                int end = mapped[0].End;
                bool contiguous = true;
                for (int index = 1; index < mapped.Length; index++)
                {
                    if (mapped[index].Start != end)
                    {
                        contiguous = false;
                        break;
                    }
                    end = mapped[index].End;
                }
                if (!contiguous) continue;

                var result = logicalLine[start..end];
                if (!string.IsNullOrWhiteSpace(result)) results.Add(result);
            }
        }

        return results.Count == 1 ? results.Single() : null;
    }

    private static bool IsRotation(
        string visualLine,
        string logicalLine,
        int rotation)
    {
        for (int index = 0; index < logicalLine.Length; index++)
        {
            if (logicalLine[index]
                != visualLine[(index + rotation) % visualLine.Length])
                return false;
        }
        return true;
    }

    private static bool ContainsArabicAndLatin(string text)
    {
        bool hasArabic = false;
        bool hasLatin = false;
        foreach (char character in text)
        {
            hasArabic |= character is >= '\u0600' and <= '\u06FF'
                or >= '\u0750' and <= '\u077F'
                or >= '\u08A0' and <= '\u08FF'
                or >= '\uFB50' and <= '\uFDFF'
                or >= '\uFE70' and <= '\uFEFF';
            hasLatin |= character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z';
        }
        return hasArabic && hasLatin;
    }

    /// <summary>
    /// Reads a non-empty text selection from the element under (<paramref name="x"/>,
    /// <paramref name="y"/>) — walking up a few levels for the TextPattern the way the feed's
    /// tweet text exposes it a level or two above the leaf under the cursor. Returns null when
    /// there's no selection there (an Explorer file row, a desktop icon, a bare button). Runs on
    /// the same worker thread as <see cref="ProbeSelectionViaUIA"/>; must not throw.
    /// </summary>
    private static (string Text, string? GestureText, bool RequireGestureText, Func<bool>? ValidateInput)? TryReadSelectionAtPoint(
        int x,
        int y,
        uint expectedProcessId,
        SelectionGesture gesture,
        bool deriveGestureText)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element == null) return null;
            if ((uint)element.Current.ProcessId != expectedProcessId) return null;
            var walker = TreeWalker.RawViewWalker;
            for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
            {
                try
                {
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                    {
                        var ranges = ((TextPattern)pat).GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            var combined = CombineSelectionRanges(ranges.Select(r => r.GetText(BrowserMessage.MaximumTextLength + 1)));
                            if (!string.IsNullOrEmpty(combined))
                            {
                                bool requireGestureText = deriveGestureText
                                    && RequiresChromiumGestureText(element, gesture);
                                var gestureText = requireGestureText
                                    ? TryReadChromiumSelectionFromGesture(
                                        (TextPattern)pat, element, gesture, combined.Length)
                                    : null;
                                return (combined, gestureText, requireGestureText,
                                    CreateInputValidation((TextPattern)pat, ranges, gestureText ?? combined));
                            }
                        }
                    }
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }
        }
        catch { /* FromPoint / UIA failure — no rescue */ }
        return null;
    }

    /// <summary>
    /// Reads the current selection via UI Automation. Returns null when no focused element,
    /// no TextPattern within the walk depth, no selection ranges, or any UIA failure. Runs on
    /// a worker thread because UIA calls can take hundreds of ms in apps where a11y is cold.
    /// </summary>
    private static SelectionProbe? CopyViaUIA(
        uint expectedProcessId, string? expectedRuntimeId, bool allowEmpty = false)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;
            if ((uint)element.Current.ProcessId != expectedProcessId) return null;
            if (!MatchesAutomationRuntimeId(element, expectedRuntimeId)) return null;
            string? focusedRuntimeId = TryReadAutomationRuntimeId(element);

            var walker = TreeWalker.RawViewWalker;
            for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
            {
                try
                {
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                    {
                        var tp = (TextPattern)pat;
                        var ranges = tp.GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            // Range reads are bounded before their result reaches the coordinator. For
                            // discontiguous selections (rare — Ctrl-click in Excel-style
                            // grids) join with \n so the caller sees all of it.
                            var combined = CombineSelectionRanges(ranges.Select(r => r.GetText(BrowserMessage.MaximumTextLength + 1)));
                            if (allowEmpty || !string.IsNullOrEmpty(combined))
                                return new SelectionProbe(SelectionProbeOutcome.HasText, combined, null, focusedRuntimeId,
                                    CreateInputValidation(tp, ranges, combined));
                        }
                    }
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }
        }
        catch { /* UIA failure */ }
        return null;
    }

    private static bool MatchesAutomationRuntimeId(
        AutomationElement element, string? expectedRuntimeId)
    {
        if (expectedRuntimeId == null) return true;
        return TryReadAutomationRuntimeId(element) == expectedRuntimeId;
    }

    private static string? TryReadAutomationRuntimeId(
        AutomationElement element)
    {
        try
        {
            int[] runtimeId = element.GetRuntimeId();
            return runtimeId.Length > 0
                ? string.Join(",", runtimeId)
                : null;
        }
        catch
        {
            return null;
        }
    }

    // Keep the oversize signal for the normal rejection path without allocating an unbounded join.
    internal static string CombineSelectionRanges(IEnumerable<string> fragments)
    {
        var result = new System.Text.StringBuilder();
        int count = 0;
        foreach (var fragment in fragments)
        {
            if (++count > 256 || result.Length + fragment.Length + (result.Length > 0 ? 1 : 0) > BrowserMessage.MaximumTextLength)
                return new string('\0', BrowserMessage.MaximumTextLength + 1);
            if (fragment.Length == 0) continue;
            if (result.Length > 0) result.Append('\n');
            result.Append(fragment);
        }
        return result.ToString();
    }

}
