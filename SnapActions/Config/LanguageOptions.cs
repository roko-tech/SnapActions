namespace SnapActions.Config;

public sealed record LanguageOption(string Code, string Name);

public static class LanguageOptions
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("en", "English"), new("ar", "العربية — Arabic"), new("fr", "French"), new("de", "German"),
        new("es", "Spanish"), new("it", "Italian"), new("pt-BR", "Portuguese (Brazil)"), new("ru", "Russian"),
        new("uk", "Ukrainian"), new("fa", "Persian"), new("ur", "Urdu"), new("tr", "Turkish"),
        new("ja", "Japanese"), new("ko", "Korean"), new("zh-CN", "Chinese (Simplified)"),
        new("zh-TW", "Chinese (Traditional)"), new("hi", "Hindi"), new("he", "Hebrew"), new("el", "Greek"),
        new("nl", "Dutch"), new("pl", "Polish"), new("sv", "Swedish"), new("id", "Indonesian")
    ];
    public static bool IsSupported(string? code) => All.Any(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
