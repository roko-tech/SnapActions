using System.Diagnostics;
using SnapActions.Actions;

namespace SnapActions.Helpers;

public static class ProcessHelper
{
    public static ActionResult TryShellOpen(string uri, string successMessage = "Opened")
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return new ActionResult(true, Message: successMessage);
        }
        catch (Exception ex)
        {
            return new ActionResult(false, Message: ex.Message);
        }
    }
}
