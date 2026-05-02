using System.Globalization;
using System.Text.RegularExpressions;

namespace SnapActions.Helpers;

/// <summary>
/// Minimal unit-conversion table for the most common everyday quantities.
/// All conversions use a "base unit" per category (m / kg / K / m³ / m/s).
/// Temperature is special-cased because Celsius/Fahrenheit are linear-with-offset, not multiplicative.
/// </summary>
public static partial class UnitConverter
{
    public enum Category { Length, Mass, Temperature, Volume, Speed }

    /// <summary>One unit's metadata: canonical symbol, factor to base, optional offset.</summary>
    public record Unit(string Symbol, Category Category, double FactorToBase, double OffsetToBase = 0);

    // Aliases map (case-insensitive). Multiple input strings → same Unit record.
    private static readonly Dictionary<string, Unit> _aliases = BuildAliases();

    public static IReadOnlyDictionary<string, Unit> Aliases => _aliases;

    private static Dictionary<string, Unit> BuildAliases()
    {
        var d = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);

        // Length — base: meter
        Unit mm = new("mm", Category.Length, 0.001);
        Unit cm = new("cm", Category.Length, 0.01);
        Unit m = new("m", Category.Length, 1.0);
        Unit km = new("km", Category.Length, 1000.0);
        Unit @in = new("in", Category.Length, 0.0254);
        Unit ft = new("ft", Category.Length, 0.3048);
        Unit yd = new("yd", Category.Length, 0.9144);
        Unit mi = new("mi", Category.Length, 1609.344);
        Add(d, mm, "mm", "millimeter", "millimeters", "millimetre", "millimetres");
        Add(d, cm, "cm", "centimeter", "centimeters", "centimetre", "centimetres");
        Add(d, m, "m", "meter", "meters", "metre", "metres");
        Add(d, km, "km", "kilometer", "kilometers", "kilometre", "kilometres");
        Add(d, @in, "in", "inch", "inches", "\"");
        Add(d, ft, "ft", "feet", "foot", "'");
        Add(d, yd, "yd", "yard", "yards");
        Add(d, mi, "mi", "mile", "miles");

        // Mass — base: kilogram
        Unit mg = new("mg", Category.Mass, 0.000001);
        Unit g = new("g", Category.Mass, 0.001);
        Unit kg = new("kg", Category.Mass, 1.0);
        Unit oz = new("oz", Category.Mass, 0.028349523125);
        Unit lb = new("lb", Category.Mass, 0.45359237);
        Unit st = new("st", Category.Mass, 6.35029318); // stone
        Unit ton = new("ton", Category.Mass, 1000.0);   // metric ton
        Add(d, mg, "mg", "milligram", "milligrams");
        Add(d, g, "g", "gram", "grams", "gm");
        Add(d, kg, "kg", "kilo", "kilos", "kilogram", "kilograms");
        Add(d, oz, "oz", "ounce", "ounces");
        Add(d, lb, "lb", "lbs", "pound", "pounds");
        Add(d, st, "st", "stone", "stones");
        Add(d, ton, "ton", "tons", "tonne", "tonnes", "t");

        // Temperature — base: kelvin. Conversion: kelvin = value*factor + offset (when factor=1, offset=K-base shift)
        // For C: K = C + 273.15. For F: K = (F - 32) * 5/9 + 273.15.
        // We model: baseValue = factor*input + offset, input = (baseValue - offset)/factor.
        Unit kelvin = new("K", Category.Temperature, 1.0, 0.0);
        Unit celsius = new("°C", Category.Temperature, 1.0, 273.15);
        Unit fahrenheit = new("°F", Category.Temperature, 5.0 / 9.0, 273.15 - (32.0 * 5.0 / 9.0));
        Add(d, kelvin, "k", "kelvin", "kelvins");
        Add(d, celsius, "c", "°c", "celsius", "centigrade");
        Add(d, fahrenheit, "f", "°f", "fahrenheit");

        // Volume — base: liter
        Unit ml = new("mL", Category.Volume, 0.001);
        Unit l = new("L", Category.Volume, 1.0);
        Unit usGal = new("gal", Category.Volume, 3.785411784);
        Unit usQt = new("qt", Category.Volume, 0.946352946);
        Unit usPt = new("pt", Category.Volume, 0.473176473);
        Unit usCup = new("cup", Category.Volume, 0.2365882365);
        Unit usFlOz = new("fl oz", Category.Volume, 0.0295735295625);
        Unit tsp = new("tsp", Category.Volume, 0.00492892159375);
        Unit tbsp = new("tbsp", Category.Volume, 0.01478676478125);
        Add(d, ml, "ml", "milliliter", "milliliters", "millilitre", "millilitres");
        Add(d, l, "l", "liter", "liters", "litre", "litres");
        Add(d, usGal, "gal", "gallon", "gallons");
        Add(d, usQt, "qt", "quart", "quarts");
        Add(d, usPt, "pt", "pint", "pints");
        Add(d, usCup, "cup", "cups");
        // Include the canonical "fl oz" symbol the regex permits. Without this, a user typing
        // "5 fl oz" matched the number-and-unit regex but failed the alias lookup and silently
        // returned no detection.
        Add(d, usFlOz, "fl oz", "fl. oz", "fl. oz.", "fl_oz", "floz", "fluid_ounce", "fluid_ounces");
        Add(d, tsp, "tsp", "teaspoon", "teaspoons");
        Add(d, tbsp, "tbsp", "tablespoon", "tablespoons");

        // Speed — base: m/s
        Unit mps = new("m/s", Category.Speed, 1.0);
        Unit kph = new("km/h", Category.Speed, 1000.0 / 3600.0);
        Unit mph = new("mph", Category.Speed, 1609.344 / 3600.0);
        Unit fps = new("ft/s", Category.Speed, 0.3048);
        Unit knot = new("kn", Category.Speed, 1852.0 / 3600.0);
        Add(d, mps, "m/s", "mps", "meters_per_second");
        Add(d, kph, "km/h", "kmh", "kph", "kilometers_per_hour");
        Add(d, mph, "mph", "miles_per_hour");
        Add(d, fps, "ft/s", "fps", "feet_per_second");
        Add(d, knot, "kn", "knot", "knots");

        return d;
    }

    private static void Add(Dictionary<string, Unit> d, Unit u, params string[] aliases)
    {
        foreach (var a in aliases) d[a] = u;
    }

    /// <summary>
    /// Try to parse "5 ft", "20°C", "100 km/h" etc. into a value + unit.
    /// </summary>
    public static bool TryParse(string text, out double value, out Unit? unit)
    {
        value = 0;
        unit = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = NumberAndUnit().Match(text.Trim());
        if (!m.Success) return false;

        var numText = m.Groups[1].Value.Replace(",", "");
        if (!double.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        var unitText = m.Groups[2].Value.Trim();
        // Normalize a few common variants.
        unitText = unitText.Replace("°", "");
        if (_aliases.TryGetValue(unitText, out var u))
        {
            unit = u;
            return true;
        }
        // Re-add the degree sign for °c/°f lookup if the bare letter didn't match a unit.
        return false;
    }

    public static double Convert(double value, Unit from, Unit to)
    {
        if (from.Category != to.Category)
            throw new ArgumentException($"Cannot convert {from.Symbol} to {to.Symbol} (different category)");
        // Source → base
        var baseVal = value * from.FactorToBase + from.OffsetToBase;
        // Base → target
        return (baseVal - to.OffsetToBase) / to.FactorToBase;
    }

    /// <summary>The set of "useful target" units for each category, in display order.</summary>
    public static IEnumerable<Unit> TargetsFor(Category cat) => cat switch
    {
        Category.Length => [_aliases["mm"], _aliases["cm"], _aliases["m"], _aliases["km"],
                            _aliases["in"], _aliases["ft"], _aliases["yd"], _aliases["mi"]],
        Category.Mass => [_aliases["mg"], _aliases["g"], _aliases["kg"], _aliases["ton"],
                          _aliases["oz"], _aliases["lb"], _aliases["st"]],
        Category.Temperature => [_aliases["c"], _aliases["f"], _aliases["k"]],
        Category.Volume => [_aliases["ml"], _aliases["l"], _aliases["tsp"], _aliases["tbsp"],
                            _aliases["cup"], _aliases["floz"], _aliases["pt"], _aliases["qt"], _aliases["gal"]],
        Category.Speed => [_aliases["m/s"], _aliases["km/h"], _aliases["mph"], _aliases["ft/s"], _aliases["kn"]],
        _ => Array.Empty<Unit>(),
    };

    [GeneratedRegex(
        // <number><optional space><unit>. Unit can include letters, slash, degree sign, quote, and spaces (for "fl oz").
        @"^(-?[\d,]+\.?\d*)\s*([°a-zA-Z/'\""][a-zA-Z/'\""\s]*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberAndUnit();
}
