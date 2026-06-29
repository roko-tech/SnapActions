namespace SnapActions.Detection.Detectors;

public interface ITextDetector
{
    TextType Type { get; }
    bool TryDetect(string text, out TextAnalysis result);
}
