using SnapActions.Actions.ContextActions;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

public class XmlFormattingTests
{
    [Theory]
    [InlineData("<!DOCTYPE root [<!ENTITY value 'hello'>]><root>&value;</root>")]
    [InlineData("<!DOCTYPE root SYSTEM 'file:///unread-local-file.dtd'><root />")]
    public void Formatting_RejectsDtdBeforeExpansionOrExternalResolution(string text)
    {
        var result = new FormatXmlAction().Execute(text, new TextClassifier().Classify(text));
        Assert.False(result.Success);
        Assert.Null(result.ResultText);
    }

    [Fact]
    public void Formatting_BoundsIndentationExpansion()
    {
        var text = string.Concat(Enumerable.Repeat("<a>", 512)) + "text"
            + string.Concat(Enumerable.Repeat("</a>", 512));
        var result = new FormatXmlAction().Execute(text, new TextClassifier().Classify(text));
        Assert.False(result.Success);
        Assert.Null(result.ResultText);
    }

    [Fact]
    public void Formatting_PreservesOrdinaryXmlAndUnicode()
    {
        const string text = "<root><child>مرحبا &amp; Hello 👋</child></root>";
        var result = new FormatXmlAction().Execute(text, new TextClassifier().Classify(text));
        Assert.True(result.Success);
        Assert.Equal($"<root>{Environment.NewLine}  <child>مرحبا &amp; Hello 👋</child>{Environment.NewLine}</root>", result.ResultText);
    }
}
