using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public partial class FormatXmlAction : IAction
{
    public string Id => "format_xml";
    public string Name => "Format XML";
    public string IconKey => "IconFormatXml";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.XmlHtml;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        if (text.Length > Core.SelectionSnapshot.MaximumTextLength)
            return new ActionResult(false, Message: "XML input exceeds 32,768 characters.");
        try
        {
            using var input = new StringReader(text.Trim());
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = Core.SelectionSnapshot.MaximumTextLength
            });
            var doc = XDocument.Load(reader);
            using var output = new BoundedXmlOutput();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
                doc.Save(writer);
            return new ActionResult(true, output.ToString(), "XML formatted");
        }
        catch (InvalidDataException)
        {
            return new ActionResult(false, Message: "Formatted XML exceeds 65,536 characters.");
        }
        catch
        {
            return new ActionResult(false, Message: "Invalid XML. DTD declarations are not supported.");
        }
    }

    // Bound output while serializing: indentation can expand even a small, DTD-free document.
    private sealed class BoundedXmlOutput : StringWriter
    {
        private void CheckLength(int additional)
        {
            if (additional > 65536 - GetStringBuilder().Length)
                throw new InvalidDataException();
        }

        public override void Write(char value) { CheckLength(1); base.Write(value); }
        public override void Write(string? value) { CheckLength(value?.Length ?? 0); base.Write(value); }
        public override void Write(char[] buffer, int index, int count) { CheckLength(count); base.Write(buffer, index, count); }
        public override void Write(ReadOnlySpan<char> buffer) { CheckLength(buffer.Length); base.Write(buffer); }
    }
}

public partial class StripTagsAction : IAction
{
    public string Id => "strip_tags";
    public string Name => "Strip Tags";
    public string IconKey => "IconFormatXml";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.XmlHtml;

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var stripped = TagRegex().Replace(text, "").Trim();
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ");
        return new ActionResult(true, stripped, "Tags stripped");
    }
}
