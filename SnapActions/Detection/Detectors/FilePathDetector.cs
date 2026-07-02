using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class FilePathDetector : ITextDetector
{
    public TextType Type => TextType.FilePath;

    // Drive-letter and UNC forms only. The previous pattern also accepted unix-style "/foo/bar"
    // selections, which almost never resolve on Windows — they just mislabeled things like
    // "/r/programming" with a FILE PATH badge and blocked other classifications.
    [GeneratedRegex(@"^([A-Za-z]:\\|\\\\)")]
    private static partial Regex PathPattern();

    // Drive letters whose type makes a synchronous existence probe safe. Network drives can hang
    // Path.Exists for tens of seconds when disconnected; optical drives can spin up. Cached per
    // letter for the session — drive types don't change while a letter stays mapped, and a wrong
    // cached verdict only costs a "could exist" (0.8-confidence) classification.
    private static readonly ConcurrentDictionary<char, bool> _probeSafeDrives = new();

    /// <summary>
    /// True when checking <paramref name="path"/> for existence cannot block on the network or on
    /// slow media: local fixed/removable/RAM drives and relative paths. False for UNC paths and
    /// for mapped network / optical / unknown drive letters — callers treat those as "could
    /// exist" and defer any real access to an explicit user action (which is when RevealInExplorer
    /// prompts for UNC).
    /// </summary>
    internal static bool IsProbeSafe(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
            return _probeSafeDrives.GetOrAdd(char.ToUpperInvariant(path[0]), IsLocalDriveType);
        return true;
    }

    private static bool IsLocalDriveType(char letter)
    {
        try
        {
            var type = new DriveInfo(letter.ToString()).DriveType;
            return type is DriveType.Fixed or DriveType.Removable or DriveType.Ram;
        }
        catch { return false; }
    }

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim().Replace("\"", "");
        if (trimmed.Contains('\n')) return false;

        if (PathPattern().IsMatch(trimmed))
        {
            // Skip Path.Exists when the probe could block — UNC (\\unreachable\share triggers SMB)
            // and mapped network / optical drive letters alike; this runs on the UI dispatcher on
            // every selection. Treat those as "could exist" without proving it.
            bool exists = IsProbeSafe(trimmed) && Path.Exists(trimmed);
            result = new TextAnalysis(TextType.FilePath, exists ? 0.98 : 0.8,
                new() { ["path"] = trimmed, ["exists"] = exists.ToString() });
            return true;
        }
        return false;
    }
}
