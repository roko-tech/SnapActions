using System.Net;
using System.Net.Http;
using SnapActions.Config;
using SnapActions.Helpers;
using SnapActions.Services;
using Xunit;

namespace SnapActions.Tests;

public class ResultPopupTests
{
    [Theory]
    [InlineData("-$33", -33, "USD")]
    [InlineData("-33 USD", -33, "USD")]
    [InlineData("USD -33", -33, "USD")]
    [InlineData("€1.234,56", 1234.56, "EUR")]
    [InlineData("1,234.56 QAR", 1234.56, "QAR")]
    [InlineData("£99.99", 99.99, "GBP")]
    [InlineData("¥10000", 10000, "JPY")]
    public void MoneyKeepsItsSignedAmountAndCurrency(string text, decimal amount, string code)
    {
        Assert.True(MoneyValue.TryParse(text, out var value));
        Assert.Equal(new MoneyValue(amount, code), value);
    }

    [Theory]
    [InlineData("2 items at $50")]
    [InlineData("100 monkeys")]
    [InlineData("$10 + $20")]
    [InlineData("-$-33")]
    [InlineData("50 USD EUR")]
    [InlineData("1..2 USD")]
    public void AmbiguousMoneyIsNotOffered(string text) => Assert.False(MoneyValue.TryParse(text, out _));

    [Fact]
    public void EveryCurrencyTokenUsesTheSameContract()
    {
        foreach (var (code, tokens) in MoneyValue.Currencies)
            foreach (var token in tokens)
            {
                Assert.True(MoneyValue.TryParse($"{token} 42", out var value));
                Assert.Equal(code, value.Currency);
            }
    }

    [Fact]
    public async Task TimeoutIsAnErrorButUserCancellationIsSilent()
    {
        var timeout = await LookupExecution.RunAsync(_ => throw new TaskCanceledException(), CancellationToken.None);
        Assert.Equal(LookupStatus.Error, timeout.Status);
        Assert.Contains("timed out", timeout.Text);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = await LookupExecution.RunAsync(_ => throw new TaskCanceledException(), cts.Token);
        Assert.Equal(LookupStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task MissingDictionaryEntryIsEmpty()
    {
        using var http = new HttpClient(new RoadmapRegressionTests.StubHandler("{}", HttpStatusCode.NotFound));
        Assert.Equal(LookupStatus.Empty, (await new LookupService(http).Define("absent", "en")).Status);
    }

    [Fact]
    public async Task OversizedBodyIsRejectedBeforeDisplay()
    {
        using var http = new HttpClient(new RoadmapRegressionTests.StubHandler(new string('x', BoundedHttp.MaxResponseBytes + 1)));
        var result = await LookupExecution.RunAsync(ct => new LookupService(http).FetchText("https://example.com", "", ct), default);
        Assert.Equal(LookupStatus.Error, result.Status);
        Assert.Contains("too large", result.Text);
    }

    [Fact]
    public void NullCollectionsAndInvalidNumbersRecoverBeforeMigration()
    {
        var settings = SettingsManager.Parse("{\"SearchEngines\":null,\"ExcludedApps\":[null],\"AppHiddenActions\":{\"editor\":null},\"PinnedActionIds\":null,\"UserActions\":[null],\"ToolbarDismissTimeout\":-1,\"MaxInlineContextActions\":1000}");
        Assert.NotEmpty(settings.SearchEngines);
        Assert.Empty(settings.PinnedActionIds);
        Assert.Empty(settings.UserActions);
        Assert.InRange(settings.ToolbarDismissTimeout, 1000, 60000);
        Assert.InRange(settings.MaxInlineContextActions, 0, 10);
    }
}
