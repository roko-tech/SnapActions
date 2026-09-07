using SnapActions.Detection;

namespace SnapActions.Core;

internal enum SelectionProviderKind { Browser, UiAutomation, ExplicitCopy, Clipboard, Manual }

/// <summary>Text and capabilities belong to one immutable operation, never to the current foreground window.</summary>
internal sealed record SelectionSnapshot(string Text, TextAnalysis Analysis, SelectionOperation Operation,
    bool CanReplace, SelectionProviderKind Provider, bool? RightToLeft = null)
{
    internal const int MaximumTextLength = 32768;
}
