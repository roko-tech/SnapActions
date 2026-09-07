using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;

namespace SnapActions.Services;

public enum LookupStatus { Success, Empty, Error, Cancelled }

public sealed record LookupResult(LookupStatus Status, string Text)
{
    public static LookupResult Success(string text) => string.IsNullOrWhiteSpace(text)
        ? new(LookupStatus.Empty, "No result found") : new(LookupStatus.Success, text);
    public static LookupResult Error(string message) => new(LookupStatus.Error, message);
}

public static class LookupExecution
{
    public static async Task<LookupResult> RunAsync(Func<CancellationToken, Task<LookupResult>> fetch, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await fetch(ct);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return new(LookupStatus.Cancelled, ""); }
        catch (OperationCanceledException)
        { return LookupResult.Error("The request timed out. Try again."); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        { return LookupResult.Error("The service quota was reached. Try again later."); }
        catch (HttpRequestException)
        { return LookupResult.Error("Couldn't reach the service. Check your connection and try again."); }
        catch (InvalidDataException ex) { return LookupResult.Error(ex.Message); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or System.Text.DecoderFallbackException or OverflowException)
        { return LookupResult.Error("The service returned an invalid response. Try again."); }
        catch (Exception ex)
        {
            Helpers.Log.Warn($"Lookup failed ({ex.GetType().Name})");
            return LookupResult.Error("The lookup could not be completed. Try again.");
        }
    }
}
