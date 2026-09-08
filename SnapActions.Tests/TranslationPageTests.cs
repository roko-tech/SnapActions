using SnapActions.Services;
using Xunit;

namespace SnapActions.Tests;

public class TranslationPageTests
{
    [Fact]
    public void SelectionLimitCountsUtf8Bytes()
    {
        Assert.True(TranslationPage.CanTranslate(new string('a', 500)));
        Assert.False(TranslationPage.CanTranslate(new string('a', 501)));
        Assert.True(TranslationPage.CanTranslate(new string('ش', 250)));
        Assert.False(TranslationPage.CanTranslate(new string('ش', 251)));
        Assert.False(TranslationPage.CanTranslate(" \r\n"));
    }

    [Fact]
    public void InitialPagePreservesTextWithoutTurningItIntoParameters()
    {
        const string text = "Hello &tl=fr <b>🙂</b>\nمرحبا + 50% #fragment";
        var uri = TranslationPage.BuildUri(text, "en", "ar");
        Assert.Equal("translate.google.com", uri.Host);
        Assert.Equal("", uri.Fragment);
        Assert.Contains("text=" + Uri.EscapeDataString(text), uri.OriginalString);
        Assert.True(TranslationPage.TryReadLanguages(uri.AbsoluteUri, out var source, out var target));
        Assert.Equal("en", source);
        Assert.Equal("ar", target);
    }

    [Fact]
    public void MissingSourceUsesDetectionAndInvalidTargetUsesEnglish()
    {
        var uri = TranslationPage.BuildUri("Hello", "", "invalid");
        Assert.Contains("sl=auto&tl=en&", uri.AbsoluteUri);
        Assert.True(TranslationPage.TryReadLanguages(uri.AbsoluteUri, out var source, out var target));
        Assert.Equal("", source);
        Assert.Equal("en", target);
    }

    [Theory]
    [InlineData("https://translate.google.com/?sl=en&tl=ar", true)]
    [InlineData("https://consent.google.com/m", true)]
    [InlineData("https://translate.google.com:443/", true)]
    [InlineData("http://translate.google.com/", false)]
    [InlineData("https://translate.google.com.evil.test/", false)]
    [InlineData("https://evil.test/translate.google.com", false)]
    [InlineData("https://user@translate.google.com/", false)]
    [InlineData("https://translate.google.com:444/", false)]
    [InlineData("https://accounts.google.com/", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("not a URL", false)]
    public void OnlyTranslationAndConsentHttpsPagesAreAllowed(string address, bool allowed) =>
        Assert.Equal(allowed, TranslationPage.IsAllowedNavigation(address));

    [Theory]
    [InlineData("https://translate.google.com/?sl=auto&tl=ar", "", "ar")]
    [InlineData("https://translate.google.com/?sl=AR&tl=EN", "ar", "en")]
    [InlineData("https://translate.google.com/?sl=iw&tl=zh-Hant", "he", "zh-TW")]
    [InlineData("https://translate.google.com/?sl=pt&tl=zh", "pt-BR", "zh-CN")]
    public void KnownLanguageChangesAreCanonicalized(string address, string expectedSource, string expectedTarget)
    {
        Assert.True(TranslationPage.TryReadLanguages(address, out var source, out var target));
        Assert.Equal(expectedSource, source);
        Assert.Equal(expectedTarget, target);
    }

    [Theory]
    [InlineData("https://evil.test/?sl=en&tl=ar")]
    [InlineData("https://consent.google.com/?sl=en&tl=ar")]
    [InlineData("https://translate.google.com/?sl=en")]
    [InlineData("https://translate.google.com/?sl=en&tl=auto")]
    [InlineData("https://translate.google.com/?sl=unsupported&tl=ar")]
    [InlineData("https://translate.google.com/?sl=en&tl=unsupported")]
    [InlineData("https://translate.google.com/?sl=en&sl=fr&tl=ar")]
    [InlineData("https://translate.google.com/?sl=en&tl=ar&tl=fr")]
    public void AmbiguousOrForeignLanguageStateDoesNotOverwritePreferences(string address) =>
        Assert.False(TranslationPage.TryReadLanguages(address, out _, out _));
}
