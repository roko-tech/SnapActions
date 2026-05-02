namespace SnapActions.Helpers;

/// <summary>
/// Parses numbers that may be in American (1,234.56) or European (1.234,56) format.
/// The currency popup uses this because user-selected text can come from anywhere — relying on
/// CurrentCulture is wrong (the *content* is in some locale, not necessarily the OS locale).
/// </summary>
public static class LocaleNumber
{
    /// <summary>
    /// Try to parse <paramref name="s"/> as a decimal number, deciding the role of `,` / `.`
    /// from the string's structure rather than CurrentCulture.
    /// Rules:
    /// - If both `,` and `.` appear, the trailing one is the decimal separator.
    /// - If only one appears: 1–2 digits after = decimal; exactly 3 digits after = thousand
    ///   separator (so "1,500" / "1.500" both mean fifteen hundred).
    /// - No separators ⇒ plain integer.
    /// </summary>
    public static bool TryParse(string s, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;

        int lastComma = s.LastIndexOf(',');
        int lastDot = s.LastIndexOf('.');
        int lastSep = System.Math.Max(lastComma, lastDot);

        string normalized;
        if (lastSep < 0)
        {
            normalized = s;
        }
        else
        {
            bool hasBoth = lastComma >= 0 && lastDot >= 0;
            int digitsAfter = s.Length - lastSep - 1;
            // With both kinds present the later one is decimal. With only one kind, 1–2 digits
            // after means decimal; exactly 3 digits means thousand separator. A 4+ digit suffix
            // is unusual but treated as decimal so something like "1,2345" still parses.
            bool isDecimal = hasBoth || digitsAfter != 3;
            if (isDecimal)
            {
                var sb = new System.Text.StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (i == lastSep) sb.Append('.');
                    else if (c != ',' && c != '.') sb.Append(c);
                }
                normalized = sb.ToString();
            }
            else
            {
                normalized = s.Replace(",", "").Replace(".", "");
            }
        }

        return double.TryParse(normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
