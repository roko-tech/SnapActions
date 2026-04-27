using System.Text;
using System.Text.Json;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class DecodeJwtAction : IAction
{
    public string Id => "decode_jwt";
    public string Name => "Decode JWT";
    public string IconKey => "IconDecode";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Jwt;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var parts = text.Trim().Split('.');
        if (parts.Length != 3) return new ActionResult(false, Message: "Not a JWT");

        try
        {
            var header = PrettyDecode(parts[0]);
            var payload = PrettyDecode(parts[1]);
            var sig = parts[2];
            var output = $"HEADER:\n{header}\n\nPAYLOAD:\n{payload}\n\nSIGNATURE:\n{sig}";
            return new ActionResult(true, output, "JWT decoded");
        }
        catch
        {
            return new ActionResult(false, Message: "Invalid JWT");
        }
    }

    private static string PrettyDecode(string base64Url)
    {
        // base64url -> base64
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        var bytes = Convert.FromBase64String(s);
        var json = Encoding.UTF8.GetString(bytes);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }
}
