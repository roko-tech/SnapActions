using System.Net;

namespace SnapActions.Detection.Detectors;

public class IpAddressDetector : ITextDetector
{
    public TextType Type => TextType.IpAddress;

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Contains(' ') || trimmed.Contains('\n')) return false;

        if (IPAddress.TryParse(trimmed, out var ip))
        {
            // Avoid matching plain integers
            if (!trimmed.Contains('.') && !trimmed.Contains(':')) return false;

            var version = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
            result = new TextAnalysis(TextType.IpAddress, 0.95,
                new() { ["ip"] = trimmed, ["version"] = version });
            return true;
        }
        return false;
    }
}
