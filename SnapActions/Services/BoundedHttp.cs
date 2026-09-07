using System.IO;
using System.Net.Http;
using System.Text;

namespace SnapActions.Services;

internal static class BoundedHttp
{
    internal const int MaxResponseBytes = 256 * 1024;

    internal static async Task<string> GetStringAsync(HttpClient http, string url, CancellationToken ct,
        int maxBytes = MaxResponseBytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException("The response is too large to display safely.");
        // HttpClient.Timeout only covers headers with ResponseHeadersRead; bound the body too.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (body.Length + count > maxBytes)
                throw new InvalidDataException("The response is too large to display safely.");
            body.Write(buffer, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(body.GetBuffer(), 0, (int)body.Length);
    }
}
