using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SnapActions.UI;
using Xunit;
using FlowDirection = System.Windows.FlowDirection;
using Size = System.Windows.Size;

namespace SnapActions.Tests;

public class PreviewDirectionTests
{
    [Fact]
    public void SearchPreview_RendersArabicRunsInReadingOrderAfterEnglishLabel()
    {
        OnSta(() =>
        {
            const string text = "قبل ChatGPT بعد";
            var preview = new TextBlock { FontSize = 20 };
            ToolbarWindow.SetPreviewContent(preview, text, "Search Google for: ", FlowDirection.RightToLeft);
            preview.Measure(new Size(800, 60));
            preview.Arrange(new Rect(0, 0, 800, 60));
            preview.UpdateLayout();

            var phrase = Assert.IsType<Span>(preview.Inlines.LastInline);
            var run = Assert.IsType<Run>(phrase.Inlines.FirstInline);
            // The quote adds one code unit to the phrase offsets. In an RTL sentence,
            // the first Arabic word belongs to the right of the English word, not its left.
            double before = run.ContentStart.GetPositionAtOffset(1)!.GetCharacterRect(LogicalDirection.Forward).X;
            double english = run.ContentStart.GetPositionAtOffset(5)!.GetCharacterRect(LogicalDirection.Forward).X;
            double after = run.ContentStart.GetPositionAtOffset(13)!.GetCharacterRect(LogicalDirection.Forward).X;
            Assert.True(before > english && english > after,
                $"Expected RTL phrase order: before={before}, English={english}, after={after}");
            Assert.Equal($"Search Google for: \"{text}\"", new TextRange(preview.ContentStart, preview.ContentEnd).Text);
        });
    }

    [Fact]
    public void SearchPreview_UsesBrowserDirectionEvenWhenSelectionStartsWithEnglish()
    {
        OnSta(() =>
        {
            var preview = new TextBlock();
            ToolbarWindow.SetPreviewContent(preview, "ChatGPT مع العربية", "Search Google for: ", FlowDirection.RightToLeft);
            Assert.Equal(FlowDirection.LeftToRight, preview.FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft, Assert.IsType<Span>(preview.Inlines.LastInline).FlowDirection);
        });
    }

    [Theory]
    [InlineData("مرحبا English", FlowDirection.RightToLeft)]
    [InlineData("(42) مرحبا English", FlowDirection.RightToLeft)]
    [InlineData("١٢٣ English عربي", FlowDirection.LeftToRight)]
    [InlineData("English ثم العربية", FlowDirection.LeftToRight)]
    [InlineData("\u200fChatGPT", FlowDirection.RightToLeft)]
    [InlineData("123 + 456", FlowDirection.LeftToRight)]
    public void PreviewDirection_UsesFirstStrongCharacterWithoutChangingText(string text, FlowDirection expected)
    {
        Assert.Equal(expected, ToolbarWindow.GetPreviewFlowDirection(text));
    }

    [Fact]
    public void PlainPreview_ResetsArabicDirectionForEnglishToast()
    {
        OnSta(() =>
        {
            var preview = new TextBlock();
            ToolbarWindow.SetPreviewContent(preview, "مرحبا ChatGPT");
            Assert.Equal(FlowDirection.RightToLeft, preview.FlowDirection);
            ToolbarWindow.SetPreviewContent(preview, "Copied to clipboard");
            Assert.Equal(FlowDirection.LeftToRight, preview.FlowDirection);
            Assert.Equal("Copied to clipboard", new TextRange(preview.ContentStart, preview.ContentEnd).Text);
        });
    }

    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF preview layout timed out");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
