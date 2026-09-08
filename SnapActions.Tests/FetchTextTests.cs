using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using SnapActions.Services;
using Xunit;

namespace SnapActions.Tests;

public class FetchTextTests
{
    [Theory]
    [InlineData("{\"data\":{\"title\":\"  مرحبا Hello 👩‍💻  \"}}", "data.title", "  مرحبا Hello 👩‍💻  ")]
    [InlineData("{\"n\":42}", "n", "42")]
    [InlineData("{\"data\":{\"n\":42}}", "data", "{\"n\":42}")]
    [InlineData("{\"items\":[1,2]}", "items", "[1,2]")]
    public async Task FetchesTheRequestedFieldThroughTheProductionService(string body, string field, string expected)
    {
        using var http = new HttpClient(new RoadmapRegressionTests.StubHandler(body));
        var result = await new LookupService(http).FetchText("https://example.test/recipe", field, default);
        Assert.Equal(new LookupResult(LookupStatus.Success, expected), result);
    }

    [Theory]
    [InlineData("{\"name\":\"x\"}", "missing")]
    [InlineData("{\"data\":{}}", "data.title")]
    [InlineData("{\"data\":42}", "data.title")]
    public async Task MissingOrNonObjectParentIsNotASuccessfulResult(string body, string field)
    {
        using var http = new HttpClient(new RoadmapRegressionTests.StubHandler(body));
        var result = await new LookupService(http).FetchText("https://example.test", field, default);
        Assert.Equal(LookupStatus.Empty, result.Status);
        Assert.Contains("not found", result.Text);
    }

    [Fact]
    public async Task RawBodyPreservesWhitespaceUnicodeAndTextBeyondTheOldHelperLimit()
    {
        string body = "  مرحبا 👩‍💻\r\n" + new string('x', 5000) + "\t  ";
        using var http = new HttpClient(new RoadmapRegressionTests.StubHandler(body));
        Assert.Equal(LookupResult.Success(body), await new LookupService(http).FetchText("https://example.test", "", default));
    }

    [Fact]
    public async Task InvalidJsonBecomesAnErrorThroughTheResultPopupExecutionPath()
    {
        using var http = new HttpClient(new RoadmapRegressionTests.StubHandler("not json"));
        var result = await LookupExecution.RunAsync(ct => new LookupService(http).FetchText("https://example.test", "title", ct), default);
        Assert.Equal(LookupStatus.Error, result.Status);
        Assert.Contains("invalid response", result.Text);
    }

    [Theory]
    [InlineData(true, 65536)]
    [InlineData(false, 65536)]
    [InlineData(true, 65538)]
    [InlineData(false, 65538)]
    public async Task ResponseLimitCountsUtf8BytesWithAndWithoutContentLength(bool knownLength, int bytes)
    {
        // Two bytes per character distinguishes a byte cap from a character cap.
        string body = new('ش', bytes / 2);
        using var stream = new ResponseStream(Encoding.UTF8.GetBytes(body));
        using var content = new StreamContent(stream);
        if (knownLength) content.Headers.ContentLength = bytes;
        else Assert.Null(content.Headers.ContentLength);
        using var http = new HttpClient(new ContentHandler(content));
        var service = new LookupService(http);
        if (bytes == 65536)
            Assert.Equal(LookupResult.Success(body), await service.FetchText("https://example.test", "", default));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => service.FetchText("https://example.test", "", default));
        Assert.Equal(!knownLength || bytes == 65536, stream.Started.Task.IsCompleted);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("relative/path")]
    public async Task InvalidUrlIsRejectedBeforeSendingARequest(string url)
    {
        var handler = new RoadmapRegressionTests.StubHandler("unreachable");
        using var http = new HttpClient(handler);
        Assert.Equal(LookupStatus.Error, (await new LookupService(http).FetchText(url, "", default)).Status);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task CancellationDuringBodyReadIsSilentAndDisposesTheStream()
    {
        using var stream = new ResponseStream([], waitForCancellation: true);
        using var http = new HttpClient(new ContentHandler(new StreamContent(stream)));
        using var cancellation = new CancellationTokenSource();
        var pending = LookupExecution.RunAsync(ct => new LookupService(http).FetchText("https://example.test", "", ct), cancellation.Token);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.Equal(new LookupResult(LookupStatus.Cancelled, ""), await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task FailedBodyReadDoesNotReturnPartialSuccessAndDisposesTheStream()
    {
        using var stream = new ResponseStream(Encoding.UTF8.GetBytes("partial"), failAfterFirstRead: true);
        using var http = new HttpClient(new ContentHandler(new StreamContent(stream)));
        var result = await LookupExecution.RunAsync(ct => new LookupService(http).FetchText("https://example.test", "", ct), default);
        Assert.Equal(LookupStatus.Error, result.Status);
        Assert.True(stream.Disposed);
    }

    private sealed class ContentHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class ResponseStream(byte[] bytes, bool waitForCancellation = false, bool failAfterFirstRead = false) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Started.TrySetResult();
            if (waitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            if (failAfterFirstRead && Position > 0) throw new IOException("Synthetic interrupted response");
            return await base.ReadAsync(buffer, ct);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
