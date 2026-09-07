using System.Net;
using System.Net.Http;
using System.Text;
using SnapActions.Actions;
using SnapActions.Actions.ContextActions;
using SnapActions.Detection;
using SnapActions.Services;
using Xunit;

namespace SnapActions.Tests;

public class RoadmapRegressionTests
{
    [Theory]
    [InlineData("100 BHD", "BHD")]
    [InlineData("100 QAR", "QAR")]
    [InlineData("100 OMR", "OMR")]
    public async Task AdvertisedCurrencyUsesItsOwnRates(string text, string code)
    {
        var handler = new StubHandler("{\"rates\":{\"EUR\":2}}");
        using var http = new HttpClient(handler);
        Assert.True(new CurrencyConverterAction().CanExecute(text, TextAnalysis.PlainText));
        var result = await new LookupService(http).ConvertCurrency(text, "EUR");
        Assert.Equal(LookupStatus.Success, result.Status);
        Assert.Contains(code, result.Text);
        Assert.EndsWith("/" + code, handler.LastUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("enc_base64_decode", "%%%")]
    [InlineData("enc_hex_decode", "GG")]
    [InlineData("enc_base64_decode", "/w==")]
    [InlineData("enc_hex_decode", "ff")]
    public void InvalidDecodeNeverProducesAnEffect(string id, string text)
    {
        var action = new ActionRegistry().GetAllActionsForCategory(ActionCategory.Encode).Single(a => a.Id == id);
        var result = action.Execute(text, TextAnalysis.PlainText);
        Assert.False(result.Success);
        Assert.Null(result.ResultText);
    }

    [Fact]
    public void TranslationLimitCountsUtf8Bytes()
    {
        string text = new('ش', 300);
        Assert.Equal(600, Encoding.UTF8.GetByteCount(text));
        Assert.False(new TranslateAction().CanExecute(text, TextAnalysis.PlainText));
    }

    [Theory]
    [InlineData("Bonjour tout le monde", "ar")]
    [InlineData("Українська мова", "en")]
    [InlineData("سلام فارسی", "en")]
    public async Task ScriptDoesNotEstablishSourceLanguage(string text, string target)
    {
        var handler = new StubHandler("{}");
        using var http = new HttpClient(handler);
        var result = await new LookupService(http).Translate(text, "", target);
        Assert.Equal(LookupStatus.Error, result.Status);
        Assert.Null(handler.LastUri);
    }

    internal sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
