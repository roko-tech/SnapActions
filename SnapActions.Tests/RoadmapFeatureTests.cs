using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapActions.Actions;
using SnapActions.Actions.ContextActions;
using SnapActions.Actions.UserActions;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.Detection;
using SnapActions.Services;
using Xunit;

namespace SnapActions.Tests;

public class RoadmapFeatureTests
{
    [Theory]
    [InlineData("https://example.com/path?q=a%2Fb&UTM_source=email&x=1&x=2#part", "https://example.com/path?q=a%2Fb&x=1&x=2#part")]
    [InlineData("https://example.com/?fbclid=abc", "https://example.com/")]
    [InlineData("https://example.com/?%75tm_source=email&ref=important", "https://example.com/?ref=important")]
    [InlineData("https://example.com/#fragment?utm_source=keep", "https://example.com/#fragment?utm_source=keep")]
    [InlineData("https://example.com/?token=a%26b&empty=&flag", "https://example.com/?token=a%26b&empty=&flag")]
    public void CleanLinksPreserveMeaningfulQueryBytes(string input, string expected) => Assert.Equal(expected, CleanLinkAction.Clean(input));

    [Theory]
    [InlineData("https://example.com/private?token=local-only")]
    [InlineData("مرحبا ChatGPT 👋")]
    public void LocalQrDecodesToTheExactOriginal(string text)
    {
        using var bytes = new MemoryStream(LocalQr.Encode(text));
        var image = BitmapFrame.Create(bytes, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var source = new ZXing.RGBLuminanceSource(pixels, converted.PixelWidth, converted.PixelHeight, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32);
        var result = new ZXing.BarcodeReaderGeneric().Decode(source);
        Assert.NotNull(result);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void QrBudgetUsesUtf8Bytes()
    {
        var text = new string('ش', 1001);
        Assert.False(new GenerateQrAction().CanExecute(text, TextAnalysis.PlainText));
        Assert.Throws<ArgumentException>(() => LocalQr.Encode(text));
    }

    [Fact]
    public void RecipeOrderAndDecodeFailureAreVisibleWithoutIntermediateEffects()
    {
        var operations = new ActionRegistry().PureTextOperations();
        var recipe = new TextRecipeDefinition { Name = "Prepare", Steps = ["ws_trim", "case_upper", "enc_base64_encode"] };
        var action = new TextRecipeAction(recipe, operations);
        Assert.Equal("SEVMTE8=", action.Execute(" hello ", TextAnalysis.PlainText).ResultText);
        recipe.Steps = ["enc_base64_decode", "case_upper"];
        var failure = action.Execute("%%%", TextAnalysis.PlainText);
        Assert.False(failure.Success); Assert.Null(failure.ResultText);
        recipe.Steps = ["delete_text"];
        Assert.False(action.Execute("hello", TextAnalysis.PlainText).Success);
    }

    [Fact]
    public void RecipeExpansionIsBounded()
    {
        var recipe = new TextRecipeDefinition { Steps = Enumerable.Repeat("enc_hex_encode", 12).ToList() };
        Assert.False(new TextRecipeAction(recipe, new ActionRegistry().PureTextOperations())
            .Execute(new string('a', 32768), TextAnalysis.PlainText).Success);
    }

    [Fact]
    public void TextInspectionLabelsBidiAndSupplementaryCodePoints()
    {
        var result = InspectTextAction.Describe("A\u202E👋\t\n");
        Assert.Contains("U+202E  RIGHT-TO-LEFT OVERRIDE", result);
        Assert.Contains("U+1F44B", result);
        Assert.Contains("Unicode code points: 5", result);
        Assert.Contains("UTF-16 units: 6", result);
        Assert.Contains("Lines: 2", result);
    }

    [Fact]
    public void SettingsRoundTripPreservesNeverRecipesAndIndependentLanguages()
    {
        var original = new AppSettings
        {
            ToolbarDismissTimeout = 0, SearchLanguage = "ar", TranslationSourceLanguage = "fr", TranslationTargetLanguage = "en",
            TextRecipes = [new() { Id = "local", Name = "تنظيف", Steps = ["ws_trim", "case_lower"] }],
            PinnedActionIds = ["recipe_local"], Theme = "light"
        };
        var parsed = SettingsManager.Parse(JsonSerializer.Serialize(original));
        Assert.Equal(0, parsed.ToolbarDismissTimeout); Assert.Equal("light", parsed.Theme);
        Assert.Equal("fr", parsed.TranslationSourceLanguage); Assert.Equal("ar", parsed.SearchLanguage);
        Assert.Contains("recipe_local", parsed.PinnedActionIds);
        Assert.Equal(original.TextRecipes[0].Steps, parsed.TextRecipes[0].Steps);
    }

    [Fact]
    public void UnicodeCustomIdsAndNullRecipeStepsSurviveSemanticRecovery()
    {
        var parsed = SettingsManager.Parse("""{"UserActions":[{"Id":"بحث","Name":"بحث","UrlTemplate":"https://example.com?q={0}"}],"PinnedActionIds":["user_بحث"],"TextRecipes":[null,{"Id":"test","Name":"Test","Steps":null}]}""");
        Assert.Single(parsed.UserActions); Assert.Contains("user_بحث", parsed.PinnedActionIds);
        Assert.Empty(Assert.Single(parsed.TextRecipes).Steps);
    }

    [Theory]
    [InlineData("Reading")]
    [InlineData("Writing")]
    [InlineData("Development")]
    public void AppPresetsOnlyReferToKnownActions(string name)
    {
        var registry = new ActionRegistry();
        var ids = registry.AllActionDescriptors().Select(a => a.Id).ToHashSet();
        var hidden = AppProfilePresets.HiddenActions(name, registry).ToList();
        Assert.NotEmpty(hidden); Assert.All(hidden, id => Assert.Contains(id, ids));
        Assert.Contains(registry.AllActionDescriptors(), a => a.Id.StartsWith("search_"));
    }

    [Fact]
    public void UiaRangeReadStopsAtTheBudgetAndNeverReturnsPartialText()
    {
        int reads = 0;
        IEnumerable<string> Ranges()
        {
            for (int i = 0; i < 1_000_000; i++) { reads++; yield return new string('x', 20000); }
        }
        var combined = UiaSelectionProvider.CombineSelectionRanges(Ranges());
        Assert.Equal(2, reads);
        Assert.Equal(UiaSelectionProvider.SelectionProbeOutcome.UntrustedText,
            UiaSelectionProvider.ClassifyUiaSelection(combined, false).Outcome);
        Assert.Equal("one\ntwo", UiaSelectionProvider.CombineSelectionRanges(["one", "", "two"]));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"1\"")]
    [InlineData("2")]
    [InlineData("true")]
    public void BrowserProtocolRejectsIncompatibleVersions(string version)
    {
        using var json = JsonDocument.Parse("{\"version\":" + version + ",\"status\":\"ok\",\"text\":\"text\",\"identity\":\"range\"}");
        Assert.Null(BrowserSelectionBridge.ParseSelection(json.RootElement));
    }

    [Fact]
    public async Task ProviderErrorsAreNotCachedAndInvalidUtf8IsAnError()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler(() => ++requests == 1
            ? "{\"responseStatus\":403}" : "{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"Hello\"}}"));
        var lookup = new LookupService(http);
        Assert.Equal(LookupStatus.Error, (await lookup.Translate("Bonjour", "fr", "en")).Status);
        Assert.Equal(LookupStatus.Success, (await lookup.Translate("Bonjour", "fr", "en")).Status);
        Assert.Equal(LookupStatus.Success, (await lookup.Translate("Bonjour", "fr", "en")).Status);
        Assert.Equal(2, requests);
        Assert.Equal(LookupStatus.Error, (await LookupExecution.RunAsync(_ => throw new System.Text.DecoderFallbackException(), default)).Status);
    }

    [Fact]
    public async Task UnsupportedDictionaryLanguageNeverSilentlyLooksUpEnglish()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler(() => { requests++; return "[]"; }));
        Assert.Equal(LookupStatus.Error, (await new LookupService(http).Define("مرحبا", "ar")).Status);
        Assert.Equal(0, requests);
    }

    [Fact]
    public void NativeManifestKeepsPathsWithSpacesAndOnlyTheAllowedOrigin()
    {
        using var manifest = JsonDocument.Parse(BrowserSetupService.ManifestJson(@"D:\Apps With Spaces\SnapActions.exe"));
        Assert.Equal(@"D:\Apps With Spaces\SnapActions.exe", manifest.RootElement.GetProperty("path").GetString());
        Assert.Equal(BrowserNativeHost.ExtensionOrigin, manifest.RootElement.GetProperty("allowed_origins")[0].GetString());
        Assert.Single(manifest.RootElement.GetProperty("allowed_origins").EnumerateArray());
    }

    private sealed class Handler(Func<string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response()) });
    }
}
