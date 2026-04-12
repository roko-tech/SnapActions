using System.Runtime.InteropServices;
using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class DeleteTextAction : IAction
{
    public string Id => "delete_text";
    public string Name => "Delete";
    public string IconKey => "IconContext";
    public ActionCategory Category => ActionCategory.Transform;

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            NativeMethods.MakeKeyInput(0x2E, false), // VK_DELETE down
            NativeMethods.MakeKeyInput(0x2E, true),   // VK_DELETE up
        };
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return new ActionResult(true);
    }
}
