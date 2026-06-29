using SnapActions.Core;
using Xunit;

namespace SnapActions.Tests;

/// <summary>
/// Pure-function tests for the scrollbar-suppression geometry. The full MouseHook needs a real
/// Windows hook to exercise; these target the extracted overloads that take a synthetic RECT
/// directly so behavior can be pinned without touching the OS.
/// </summary>
public class MouseHookGeometryTests
{
    // 1000x800 window at the screen origin — easy to reason about.
    private static readonly MouseHook.RECT Rect = new() { left = 0, top = 0, right = 1000, bottom = 800 };

    private static MouseHook.POINT P(int x, int y) => new() { X = x, Y = y };

    // ── LooksLikeScrollbarDrag ───────────────────────────────────────────────

    [Fact]
    public void Drag_RightEdge_VerticalMotion_LTR_IsScrollbar()
    {
        // Both endpoints in the rightmost 25 px, motion almost purely vertical.
        Assert.True(MouseHook.LooksLikeScrollbarDrag(P(985, 100), P(990, 600), Rect, isRtl: false));
    }

    [Fact]
    public void Drag_LeftEdge_VerticalMotion_RTL_IsScrollbar()
    {
        // RTL flip: scrollbar is on the LEFT in mirrored layouts.
        Assert.True(MouseHook.LooksLikeScrollbarDrag(P(10, 100), P(15, 600), Rect, isRtl: true));
    }

    [Fact]
    public void Drag_LeftEdge_LTR_NotScrollbar()
    {
        // Left edge in a non-RTL layout shouldn't trigger the heuristic.
        Assert.False(MouseHook.LooksLikeScrollbarDrag(P(10, 100), P(15, 600), Rect, isRtl: false));
    }

    [Fact]
    public void Drag_RightEdge_RTL_NotScrollbar()
    {
        // RTL flips the vertical-scrollbar edge to the left, so a right-edge drag is no longer
        // suppressed when WS_EX_LAYOUTRTL is set.
        Assert.False(MouseHook.LooksLikeScrollbarDrag(P(985, 100), P(990, 600), Rect, isRtl: true));
    }

    [Fact]
    public void Drag_BottomEdge_HorizontalMotion_IsScrollbar()
    {
        // Both endpoints in the bottom 25 px, motion almost purely horizontal.
        Assert.True(MouseHook.LooksLikeScrollbarDrag(P(100, 785), P(700, 790), Rect, isRtl: false));
    }

    [Fact]
    public void Drag_AtEdge_ButDiagonal_NotScrollbar()
    {
        // Both endpoints at the right edge but a roughly 45° drag — vertical motion not >> 3×
        // horizontal motion, so it doesn't fit the scrollbar shape.
        Assert.False(MouseHook.LooksLikeScrollbarDrag(P(985, 100), P(995, 110), Rect, isRtl: false));
    }

    [Fact]
    public void Drag_NotAtEdge_NotScrollbar()
    {
        // Vertical drag deep inside the window — column selection in an IDE, not a scrollbar.
        Assert.False(MouseHook.LooksLikeScrollbarDrag(P(500, 100), P(500, 600), Rect, isRtl: false));
    }

    [Fact]
    public void Drag_OneEndpointAwayFromEdge_NotScrollbar()
    {
        // Only the down endpoint is at the edge; user dragged inward — that's a real selection
        // starting on the scrollbar slop area.
        Assert.False(MouseHook.LooksLikeScrollbarDrag(P(985, 100), P(500, 600), Rect, isRtl: false));
    }

    // ── LooksLikeScrollbarPosition (single-point, for long-press) ───────────

    [Fact]
    public void Position_RightEdge_LTR_IsScrollbar()
    {
        Assert.True(MouseHook.LooksLikeScrollbarPosition(P(985, 400), Rect, isRtl: false));
    }

    [Fact]
    public void Position_LeftEdge_RTL_IsScrollbar()
    {
        Assert.True(MouseHook.LooksLikeScrollbarPosition(P(15, 400), Rect, isRtl: true));
    }

    [Fact]
    public void Position_LeftEdge_LTR_NotScrollbar()
    {
        Assert.False(MouseHook.LooksLikeScrollbarPosition(P(15, 400), Rect, isRtl: false));
    }

    [Fact]
    public void Position_BottomEdge_IsScrollbar()
    {
        Assert.True(MouseHook.LooksLikeScrollbarPosition(P(500, 785), Rect, isRtl: false));
    }

    [Fact]
    public void Position_Center_NotScrollbar()
    {
        Assert.False(MouseHook.LooksLikeScrollbarPosition(P(500, 400), Rect, isRtl: false));
    }

    // ── System-metric-derived thresholds ─────────────────────────────────────
    //
    // We can't pin a specific number — SM_CXDRAG and SM_CXDOUBLECLK depend on the OS / user
    // settings — but we can check the resolved value lands in a sensible window. If the
    // GetSystemMetrics P/Invoke ever returns 0 (headless runner / very stripped image), the
    // ComputeSquaredThreshold fallback to 4 px keeps the value in this range.

    [Fact]
    public void LongPressMoveCancelDistSq_InSensibleRange()
    {
        // Typical Windows defaults give 4 px → 16. Allow up to 32 px (huge custom drag rect,
        // e.g. touch-optimized) but reject anything that would let an 8 px drag slip past us
        // (the bug we're fixing).
        Assert.InRange(MouseHook.LongPressMoveCancelDistSq, 9, 32 * 32);
    }

    [Fact]
    public void MultiClickRadiusSq_InSensibleRange()
    {
        // Typical Windows defaults give 4 px → 16. Same upper bound as drag, and a floor of
        // 9 (3 px) so an absurdly tight metric doesn't break legitimate double-clicks.
        Assert.InRange(MouseHook.MultiClickRadiusSq, 9, 32 * 32);
    }
}
