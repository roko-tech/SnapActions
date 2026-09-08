using System.Windows;
using SnapActions.Core;
using Xunit;

namespace SnapActions.Tests;

public class MultilineSelectionGeometryTests
{
    private static readonly Rect FirstLine = new(1557, 1511, 605, 54);
    private static readonly Rect LastLine = new(1557, 1576, 658, 54);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullTwoLineSelectionMatchesForwardAndReverseGesture(bool reverse)
    {
        // Recorded Codex geometry: the native range includes both sentences and the final period.
        // Its extra one-pixel newline rectangle must not replace either real selected row.
        var selection = new[] { FirstLine, new Rect(2161, 1511, 1, 54), LastLine };
        Assert.True(Matches(selection, 1556, 1540, 2220, 1605, reverse));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialBoundaryLinesMatchOnlyTheSelectedParts(bool reverse)
    {
        Rect[] selection = [new(1720, 1511, 442, 54), new(1557, 1576, 302, 54)];
        Assert.True(Matches(selection, 1724, 1540, 1855, 1605, reverse));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoftWrapKeepsTheSelectedSpaceOutsideTheVisibleLineText(bool reverse)
    {
        // Chromium excludes the wrapping space from TextUnit.Line but includes its rectangle
        // in the complete native selection; dropping it changes the source paragraph.
        Rect first = new(1385, 408, 780, 54), last = new(1385, 462, 792, 54);
        Rect[] selection = [first, new(2164, 408, 12, 54), last];
        Assert.True(UiaSelectionProvider.MatchesMultilineSelectionGeometry(selection,
            [reverse ? last : first], [reverse ? first : last],
            reverse ? new(true, 1, 2180, 490, 1384, 438) : new(true, 1, 1384, 438, 2180, 490)));
    }

    [Fact]
    public void DragBeyondLineEdgesKeepsTheFullNativeSelection()
    {
        Assert.True(Matches([FirstLine, LastLine], 1500, 1540, 2300, 1605));
    }

    [Fact]
    public void BidirectionalFragmentsCanCoverTheBoundaryLines()
    {
        Rect[] selection = [new(1700, 1511, 170, 54), new(1990, 1511, 172, 54),
            new(1557, 1576, 110, 54), new(1840, 1576, 190, 54)];
        Assert.True(Matches(selection, 1703, 1540, 2026, 1605));
    }

    [Fact]
    public void InteriorLinesAndBlankLineGapsDoNotClipTheSelection()
    {
        Rect first = new(100, 10, 400, 20), last = new(100, 100, 300, 20);
        Rect[] selection = [first, new(100, 40, 350, 20), last];
        Assert.True(UiaSelectionProvider.MatchesMultilineSelectionGeometry(selection, [first], [last],
            new(true, 1, 100, 20, 400, 110)));
    }

    [Fact]
    public void FirstLineOnlyCannotStandForTheWholeMultilineSelection()
    {
        Assert.False(Matches([FirstLine], 1556, 1540, 2220, 1605));
    }

    [Fact]
    public void CaretOnSecondLineDoesNotProveThatItsTextWasCaptured()
    {
        Assert.False(Matches([FirstLine, new Rect(2214, 1576, 1, 54)], 1556, 1540, 2220, 1605));
    }

    [Fact]
    public void UnrelatedEarlierSelectionIsRejected()
    {
        // Recorded delayed UIA response pointed at unrelated content above the real selection.
        Assert.False(Matches([new Rect(3335, 419, 41, 41), new Rect(1612, 509, 1, 54)],
            1556, 1540, 2220, 1605));
    }

    [Fact]
    public void WholeLineSelectionCannotReplaceAPartialGesture()
    {
        Assert.False(Matches([FirstLine, LastLine], 1800, 1540, 1900, 1605));
    }

    [Fact]
    public void RecordedPartialArabicRangeWithAnExtraLetterIsRejected()
    {
        // This provider range starts one letter before the drag and loses its newline.
        // The distinct selected edge must not be accepted by widening the endpoint tolerance.
        Assert.False(UiaSelectionProvider.MatchesMultilineSelectionGeometry(
            [new(2021, 846, 316, 54), new(2259, 900, 192, 54)],
            [new(2021, 846, 430, 54)], [new(2082, 900, 369, 54)],
            new(true, 1, 2321, 876, 2260, 930)));
    }

    [Fact]
    public void MissingLastWordFailsTheEndpointCheck()
    {
        Assert.False(Matches([FirstLine, new Rect(1557, 1576, 500, 54)], 1556, 1540, 2220, 1605));
    }

    [Fact]
    public void SelectedTextOutsideTheBoundaryLineIsRejected()
    {
        Assert.False(Matches([new Rect(1450, 1511, 712, 54), LastLine], 1556, 1540, 2220, 1605));
    }

    [Fact]
    public void ExtraSelectedRowsOutsideTheGestureAreRejected()
    {
        Assert.False(Matches([FirstLine, LastLine, new Rect(1557, 1641, 300, 54)],
            1556, 1540, 2220, 1605));
    }

    [Fact]
    public void StaleLineLocationsCannotValidateTheGesture()
    {
        Assert.False(Matches([FirstLine, LastLine], 1556, 1460, 2220, 1605));
    }

    [Fact]
    public void ExpandedRangeContainingBothVisualRowsUsesTheCompleteNativeSelection()
    {
        var gesture = new UiaSelectionProvider.SelectionGesture(true, 1, 1556, 1540, 2220, 1605);
        Assert.False(UiaSelectionProvider.HasSingleVisualLineGeometry([FirstLine, LastLine], gesture));
        Assert.True(UiaSelectionProvider.MatchesMultilineSelectionGeometry([FirstLine, LastLine],
            [FirstLine, LastLine], [FirstLine, LastLine], gesture));
    }

    [Fact]
    public void SameLineGeometryCannotClipADragWhoseEndpointIsOnAnotherRow()
    {
        Assert.False(UiaSelectionProvider.HasSingleVisualLineGeometry([FirstLine],
            new(true, 1, 1556, 1540, 2220, 1605)));
    }

    [Fact]
    public void SameLineBidirectionalFragmentsRemainOnTheExistingGeometryPath()
    {
        Rect[] line = [new(1557, 1511, 300, 54), new(1900, 1511, 262, 54), new(1856, 1511, 1, 54)];
        Assert.True(UiaSelectionProvider.HasSingleVisualLineGeometry(line,
            new(true, 1, 1600, 1540, 2100, 1542)));
    }

    [Fact]
    public void SingleLineAndNonDragGesturesStayOnTheirExistingPaths()
    {
        Assert.False(UiaSelectionProvider.MatchesMultilineSelectionGeometry([FirstLine, LastLine],
            [FirstLine], [FirstLine], new(true, 1, 1556, 1540, 2160, 1540)));
        Assert.False(UiaSelectionProvider.MatchesMultilineSelectionGeometry([FirstLine, LastLine],
            [FirstLine], [LastLine], new(false, 2, 1556, 1540, 2220, 1605)));
    }

    [Fact]
    public void EmptyOrNonfiniteGeometryCannotValidateSelection()
    {
        Assert.False(Matches([], 1556, 1540, 2220, 1605));
        Assert.False(Matches([Rect.Empty, new Rect(1557, 1576, double.PositiveInfinity, 54)],
            1556, 1540, 2220, 1605));
    }

    private static bool Matches(Rect[] selection, int startX, int startY, int endX, int endY, bool reverse = false) =>
        UiaSelectionProvider.MatchesMultilineSelectionGeometry(selection,
            [reverse ? LastLine : FirstLine], [reverse ? FirstLine : LastLine],
            reverse ? new(true, 1, endX, endY, startX, startY) : new(true, 1, startX, startY, endX, endY));
}
