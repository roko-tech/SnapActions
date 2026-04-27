using System.IO;
using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class OpenFilePathAction : IAction
{
    public string Id => "open_filepath";
    public string Name => "Open";
    public string IconKey => "IconOpenFile";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) =>
        analysis.Type == TextType.FilePath && Path.Exists(CleanPath(text));

    public ActionResult Execute(string text, TextAnalysis analysis) =>
        ProcessHelper.TryOpenLocalPath(CleanPath(text));

    internal static string CleanPath(string text) => text.Trim().Replace("\"", "");
}

public class OpenContainingFolderAction : IAction
{
    public string Id => "open_folder";
    public string Name => "Open Folder";
    public string IconKey => "IconFolder";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        if (analysis.Type != TextType.FilePath) return false;
        var path = OpenFilePathAction.CleanPath(text);
        // File exists, dir exists, OR parent dir exists (so we can reveal a missing file's folder).
        return File.Exists(path) || Directory.Exists(path) ||
               Directory.Exists(System.IO.Path.GetDirectoryName(path));
    }

    public ActionResult Execute(string text, TextAnalysis analysis) =>
        ProcessHelper.RevealInExplorer(OpenFilePathAction.CleanPath(text));
}
