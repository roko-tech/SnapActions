using System.IO;
using System.Text;
using System.Text.Json;
using SnapActions.Core;
using Xunit;

namespace SnapActions.Tests;

public class BrowserSelectionTests
{
    [Theory]
    [InlineData("rtl", true)]
    [InlineData("ltr", false)]
    [InlineData("auto", null)]
    [InlineData(null, null)]
    public void BrowserSelection_PreservesSourceDirectionWhenAvailable(string? direction, bool? expected)
    {
        using var message = JsonDocument.Parse(JsonSerializer.Serialize(new {
            version = 1, status = "ok", text = "ChatGPT مع العربية", identity = "range", direction
        }));
        var selection = BrowserSelectionBridge.ParseSelection(message.RootElement);
        Assert.NotNull(selection);
        Assert.Equal(expected, selection.RightToLeft);
        Assert.Equal("ChatGPT مع العربية", selection.Text);
    }

    [Fact]
    public async Task BrowserValidation_RejectsChangedSelectionAndSurvivesTargetBinding()
    {
        var source = new SelectionOperationSource();
        bool selected = true;
        var operation = source.Begin(default).WithSelectionValidation(() => Task.FromResult(selected));
        Assert.True(await operation.WithTarget(default).CanUseSelectionAsync());
        selected = false;
        Assert.False(await operation.WithTarget(default).CanUseSelectionAsync());
    }

    [Fact]
    public async Task BrowserValidation_NewerOperationWinsWhileReplyIsPending()
    {
        var source = new SelectionOperationSource();
        var completion = new TaskCompletionSource<bool>();
        var operation = source.Begin(default).WithSelectionValidation(() => completion.Task);
        var pending = operation.CanUseSelectionAsync();
        source.Begin(default);
        completion.SetResult(true);
        Assert.False(await pending);
    }

    [Fact]
    public async Task NativeMessages_RoundTripMixedTextAndSeparateConsecutiveFrames()
    {
        const string text = "استخدم ChatGPT (GPT-5)، الإصدار 2.0!\nثم جرّب English مع العربية 👋";
        using var stream = new MemoryStream();
        var first = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { text }));
        await BrowserMessage.WriteAsync(stream, first, default);
        await BrowserMessage.WriteAsync(stream, BrowserMessage.Encode(new { text = "next" }), default);
        stream.Position = 0;
        var received = await BrowserMessage.ReadAsync(stream, default);
        Assert.Equal(first, received);
        Assert.Equal(text, JsonDocument.Parse(received!).RootElement.GetProperty("text").GetString());
        Assert.NotNull(await BrowserMessage.ReadAsync(stream, default));
        Assert.Null(await BrowserMessage.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BrowserMessage.MaximumBytes + 1)]
    public async Task NativeMessages_RejectInvalidLengthsBeforeAllocatingPayload(int length)
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(length));
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserMessage.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(new byte[] { 2 })]
    [InlineData(new byte[] { 2, 0, 0, 0, 65 })]
    public async Task NativeMessages_RejectTruncatedHeaderOrPayload(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserMessage.ReadAsync(stream, default));
    }

    [Fact]
    public void BrowserSelection_PreservesWhitespacePunctuationAndDirectionMarks()
    {
        const string text = "  مرحباً \u2066ChatGPT\u2069 (42)\n English!  ";
        using var message = JsonDocument.Parse(JsonSerializer.Serialize(new {
            version = 1, status = "ok", text, identity = "document:frame:range", editable = true
        }));
        var result = BrowserSelectionBridge.ParseSelection(message.RootElement);
        Assert.NotNull(result);
        Assert.Equal(text, result.Text);
        Assert.True(result.Editable);
        Assert.True(result.Handled);
    }

    [Theory]
    [InlineData("{\"version\":1,\"status\":\"inactive\",\"text\":\"stale\",\"identity\":\"old\"}")]
    [InlineData("{\"version\":1,\"status\":\"ok\",\"text\":\"stale\"}")]
    [InlineData("{\"version\":1,\"status\":\"ok\",\"text\":42,\"identity\":\"old\"}")]
    [InlineData("{\"version\":1,\"status\":42,\"text\":\"stale\",\"identity\":\"old\"}")]
    [InlineData("{\"version\":1,\"status\":\"ok\",\"text\":\"stale\",\"identity\":\"\"}")]
    [InlineData("{\"status\":\"ok\",\"text\":\"stale\",\"identity\":\"old\"}")]
    [InlineData("{\"version\":2,\"status\":\"ok\",\"text\":\"stale\",\"identity\":\"old\"}")]
    [InlineData("null")]
    public void BrowserSelection_RejectsInactiveOrMalformedReplies(string json)
    {
        using var message = JsonDocument.Parse(json);
        Assert.Null(BrowserSelectionBridge.ParseSelection(message.RootElement));
    }

    [Fact]
    public void BrowserSelection_RejectsOversizedSelection()
    {
        using var message = JsonDocument.Parse(JsonSerializer.Serialize(new {
            version = 1, status = "ok", text = new string('a', BrowserMessage.MaximumTextLength + 1), identity = "range"
        }));
        Assert.Null(BrowserSelectionBridge.ParseSelection(message.RootElement));
    }
}
