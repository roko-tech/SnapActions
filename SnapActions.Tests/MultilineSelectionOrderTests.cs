using SnapActions.Core;
using Xunit;

namespace SnapActions.Tests;

public class MultilineSelectionOrderTests
{
    private const string FirstArabic = "الكتاب الأزرق على الطاولة.";
    private const string LastArabic = "المصباح بجانب النافذة.";

    [Fact]
    public void RecordedArabicLineBreakUsesItsProvenLogicalPosition()
    {
        string firstVisual = "\n" + FirstArabic;
        string logical = FirstArabic + "\n" + LastArabic;
        Assert.Equal(logical, Map(firstVisual, LastArabic, logical));
    }

    [Fact]
    public void PartialArabicEndpointsKeepTheOriginalLineSeparator()
    {
        string firstVisual = "\n" + FirstArabic;
        string logical = FirstArabic + "\n" + LastArabic;
        const int offset = 6, lastLength = 12;
        string expected = logical[(offset - 1)..(FirstArabic.Length + 1 + lastLength)];
        Assert.Equal(expected,
            UiaSelectionProvider.MapMultilineSelectionToLogicalText(firstVisual, LastArabic,
                new(offset, firstVisual.Length - offset), new(0, lastLength), logical, expected));
    }

    [Fact]
    public void MixedDirectionLineRotationsKeepLogicalReadingOrder()
    {
        const string first = "ChatGPT مع العربية\n";
        const string last = "English والنص العربي";
        string visualFirst = first[8..] + first[..8];
        string visualLast = last[8..] + last[..8];
        Assert.Equal(first + last, Map(visualFirst, visualLast, first + last));
    }

    [Fact]
    public void CrLfSeparatorsArePreservedExactly()
    {
        string firstVisual = "\r\n" + FirstArabic;
        string logical = FirstArabic + "\r\n" + LastArabic;
        Assert.Equal(logical, Map(firstVisual, LastArabic, logical));
    }

    [Fact]
    public void SoftWrappedSpaceIsRetainedWithoutAddingANewline()
    {
        const string first = "The green bicycle is near the station and the";
        const string last = "yellow umbrella is beside the wooden bench.";
        Assert.Equal(first + " " + last, Map(first, last, first + " " + last, first + " " + last));
    }

    [Fact]
    public void ParagraphSeparatorsComeFromTheLogicalSource()
    {
        const string first = "First sentence.";
        const string last = "Second sentence.";
        Assert.Equal(first + "\n\n" + last, Map(first, last, first + "\n\n" + last, first + "\n\n" + last));
    }

    [Theory]
    [InlineData("A\n\nB", "\nAB")]
    [InlineData("A\n\tB", "\nA B")]
    [InlineData("A\r\nB", "\nA B")]
    public void LogicalNameCannotAddOrSubstituteSelectedWhitespace(string logical, string selected)
    {
        Assert.Null(Map("\nA", "B", logical, selected));
    }

    [Fact]
    public void UnprovenMiddleProseCannotBeInserted()
    {
        Assert.Null(Map(FirstArabic, LastArabic, FirstArabic + "\nUnselected text\n" + LastArabic));
    }

    [Fact]
    public void SameCharacterInventoryWithoutLineRotationProofIsRejected()
    {
        Assert.Null(Map("abc\n", "def", "acb\ndef"));
    }

    [Fact]
    public void RepeatedLinesMustIdentifyOneSourceSpan()
    {
        Assert.Null(Map("same", "same", "same\nsame\nsame"));
        Assert.Equal("same\nsame", Map("same", "same", "same\nsame", "same\nsame"));
    }

    [Fact]
    public void NoncontiguousPartialRotationIsNotGuessed()
    {
        Assert.Null(UiaSelectionProvider.MapMultilineSelectionToLogicalText("\n" + FirstArabic, LastArabic,
            new(0, 5), new(0, LastArabic.Length), FirstArabic + "\n" + LastArabic,
            "\n" + FirstArabic[..4] + LastArabic));
    }

    [Fact]
    public void OversizedLineAndInvalidSelectionStayUnavailable()
    {
        Assert.Null(Map(new string('a', 513), LastArabic, new string('a', 513) + LastArabic));
        Assert.Null(UiaSelectionProvider.MapMultilineSelectionToLogicalText(FirstArabic, LastArabic,
            new(-1, 2), new(0, 2), FirstArabic + "\n" + LastArabic, FirstArabic + "\n" + LastArabic));
    }

    private static string? Map(string first, string last, string logical, string? selected = null) =>
        UiaSelectionProvider.MapMultilineSelectionToLogicalText(first, last,
            new(0, first.Length), new(0, last.Length), logical, selected ?? first + last);
}
