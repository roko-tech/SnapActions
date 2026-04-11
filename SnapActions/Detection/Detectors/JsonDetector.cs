using System.Text.Json;

namespace SnapActions.Detection.Detectors;

public class JsonDetector : ITextDetector
{
    public TextType Type => TextType.Json;

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 2) return false;
        if (trimmed[0] != '{' && trimmed[0] != '[') return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var kind = doc.RootElement.ValueKind == JsonValueKind.Array ? "array" : "object";
            result = new TextAnalysis(TextType.Json, 0.95, new() { ["kind"] = kind });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
