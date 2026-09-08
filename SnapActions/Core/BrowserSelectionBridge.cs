using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using SnapActions.Helpers;

namespace SnapActions.Core;

internal sealed class BrowserSelectionBridge : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Peer, byte> _peers = new();

    internal sealed record Selection(bool Handled, string? Text = null, bool Editable = false,
        string? Identity = null, Peer? Source = null, bool? RightToLeft = null);

    internal void Start() => _ = AcceptAsync();

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(BrowserNativeHost.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                uint browserPid = GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientPid)
                    ? BrowserNativeHost.FindBrowserProcess(clientPid) : 0;
                if (browserPid == 0) { pipe.Dispose(); continue; }
                var peer = new Peer(pipe, browserPid);
                _peers.TryAdd(peer, 0);
                Volatile.Write(ref CaptureDiagnostics.ConnectedBrowsers, _peers.Count);
                Log.Info($"Browser selection bridge connected (browser PID {browserPid})");
                _ = ReadPeerAsync(peer);
                pipe = null; // Owned by ReadPeerAsync from here.
            }
            catch (OperationCanceledException) { break; }
            catch (IOException ex)
            {
                Log.Error("Browser selection bridge listener", ex);
                break;
            }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task ReadPeerAsync(Peer peer)
    {
        try
        {
            while (await BrowserMessage.ReadAsync(peer.Pipe, _stop.Token) is { } bytes)
            {
                using var document = JsonDocument.Parse(bytes);
                var reply = document.RootElement;
                if (reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long requestId)
                    && peer.Pending.TryRemove(requestId, out var pending))
                    pending.TrySetResult(reply.Clone());
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            _peers.TryRemove(peer, out _);
            Volatile.Write(ref CaptureDiagnostics.ConnectedBrowsers, _peers.Count);
            peer.Dispose();
            Log.Info($"Browser selection bridge disconnected (browser PID {peer.BrowserProcessId})");
        }
    }

    internal async Task<Selection> CaptureAsync(ForegroundTarget target)
    {
        var peers = _peers.Keys.Where(peer => peer.BrowserProcessId == target.ProcessId).ToArray();
        if (peers.Length == 0) { CaptureDiagnostics.SetStatus("No companion connected for this app; trying UIA"); return new Selection(false); }
        if (!ForegroundGuard.StillValid(target)) return new Selection(true);
        var selections = await Task.WhenAll(peers.Select(async peer =>
            ParseSelection(await peer.RequestAsync(_stop.Token), peer)));
        if (!ForegroundGuard.StillValid(target)) return new Selection(true);
        var active = selections.Where(selection => selection != null).ToArray();
        if (active.Length != 1) CaptureDiagnostics.SetStatus("Browser page unavailable, empty, unfocused, or companion needs reloading");
        // Multiple profiles may share a browser PID; only the focused profile may answer.
        return active.Length == 1 ? active[0]! : new Selection(true);
    }

    internal async Task<bool> StillSelectedAsync(Selection selection, ForegroundTarget target)
    {
        if (selection.Source == null) return false;
        var before = ForegroundGuard.Capture();
        if (!ForegroundGuard.MatchesWindow(target, before))
        {
            Log.Info($"Browser selection rejected before validation (foreground match: {target.ForegroundWindow == before.ForegroundWindow}, focused child match: {target.FocusedWindow == before.FocusedWindow}, process match: {target.ProcessId == before.ProcessId}, thread match: {target.ThreadId == before.ThreadId})");
            return false;
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var reply = await selection.Source.RequestAsync(_stop.Token, selection.Identity);
        var current = ParseSelection(reply, selection.Source);
        CaptureDiagnostics.Record("Browser validation", started);
        if (current == null)
        {
            string status = reply is { ValueKind: JsonValueKind.Object } message
                && message.TryGetProperty("status", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "missing" : "no reply";
            status = status switch
            {
                "inactive" or "unavailable" or "empty" or "incompatible" or "no reply" => status,
                _ => "invalid reply"
            };
            Log.Info($"Browser selection rejected by companion ({status})");
        }
        else if (current.Text != selection.Text || current.Identity != selection.Identity)
            Log.Info($"Browser selection changed (text match: {current.Text == selection.Text}, range match: {current.Identity == selection.Identity})");
        return current != null && current.Text == selection.Text && current.Identity == selection.Identity
            && ForegroundGuard.StillValid(target);
    }

    internal static Selection? ParseSelection(JsonElement? message, Peer? peer = null)
    {
        if (message is not { } reply || reply.ValueKind != JsonValueKind.Object
            || !reply.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var protocol) || protocol != BrowserMessage.ProtocolVersion
            || !reply.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
            || status.GetString() != "ok") return null;
        if (!reply.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String
            || !reply.TryGetProperty("identity", out var identity) || identity.ValueKind != JsonValueKind.String)
            return null;
        string value = text.GetString()!;
        string key = identity.GetString()!;
        if (value.Length > BrowserMessage.MaximumTextLength || key.Length == 0 || key.Length > 8192) return null;
        bool editable = reply.TryGetProperty("editable", out var edit) && edit.ValueKind == JsonValueKind.True;
        bool? rightToLeft = reply.TryGetProperty("direction", out var direction)
            && direction.ValueKind == JsonValueKind.String
            ? direction.GetString() switch { "rtl" => true, "ltr" => false, _ => (bool?)null }
            : null;
        return new Selection(true, value, editable, key, peer, rightToLeft);
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var peer in _peers.Keys) peer.Dispose();
    }

    internal sealed class Peer(NamedPipeServerStream pipe, uint browserProcessId) : IDisposable
    {
        internal NamedPipeServerStream Pipe { get; } = pipe;
        internal uint BrowserProcessId { get; } = browserProcessId;
        internal ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> Pending { get; } = new();
        private readonly SemaphoreSlim _write = new(1, 1);
        private long _requestId;

        internal async Task<JsonElement?> RequestAsync(CancellationToken cancellationToken, string? identity = null)
        {
            long id = Interlocked.Increment(ref _requestId);
            var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending[id] = completion;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(1000);
            try
            {
                await _write.WaitAsync(timeout.Token);
                try { await BrowserMessage.WriteAsync(Pipe, BrowserMessage.Encode(new { id, type = "selection", version = BrowserMessage.ProtocolVersion, identity }), timeout.Token); }
                finally { _write.Release(); }
                return await completion.Task.WaitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { return null; }
            finally { Pending.TryRemove(id, out _); }
        }

        public void Dispose()
        {
            Pipe.Dispose();
            foreach (var request in Pending.Values) request.TrySetResult(null);
            Pending.Clear();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
}
