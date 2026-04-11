using System.Text.RegularExpressions;
using System.Xml.Linq;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public partial class FormatXmlAction : IAction
{
    public string Id => "format_xml";
    public string Name => "Format XML";
    public string IconKey => "IconFormatXml";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.XmlHtml;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        try
        {
            var doc = XDocument.Parse(text.Trim());
            return new ActionResult(true, doc.ToString(), "XML formatted");
        }
        catch
        {
            return new ActionResult(false, Message: "Invalid XML");
        }
    }
}

public partial class StripTagsAction : IAction
{
    public string Id => "strip_tags";
    public string Name => "Strip Tags";
    public string IconKey => "IconStripTags";
    public ActionCategory Category => ActionCategory.Context;

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
