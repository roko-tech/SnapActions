namespace SnapActions.Detection;

public record TextAnalysis(
    TextType Type,
    double Confidence,
    Dictionary<string, string>? Metadata = null
)
{
    public static readonly TextAnalysis PlainText = new(TextType.PlainText, 1.0);
}
