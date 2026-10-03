using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeTool.Core;

/// <summary>Reads, scales, converts, and combines the quantities at the start of ingredient lines.</summary>
public static class IngredientMath
{
    private const string FractionChars = "½⅓⅔¼¾⅛⅜⅝⅞";
    private const string Number =
        @"(?:\d+\s*[" + FractionChars + @"]|\d+\s+\d+/\d+|\d+/\d+|\d+(?:\.\d+)?|[" + FractionChars + @"])";

    private static readonly Regex Leading = new(
        @"^(?<lead>\s*)(?<first>" + Number + @")(?:\s*(?:-|–|—|to)\s*(?<second>" + Number + @"))?(?<rest>(?:\s.*|[A-Za-z].*)?)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex Fahrenheit = new(
        @"(?<n>\d{2,3})\s*(?:°|º|degrees?)\s*F\b(?!\s*\()", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Celsius = new(
        @"(?<n>\d{2,3})\s*(?:°|º|degrees?)\s*C\b(?!\s*\()", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Dictionary<char, double> FractionValues = new()
    {
        ['½'] = 1.0 / 2, ['⅓'] = 1.0 / 3, ['⅔'] = 2.0 / 3, ['¼'] = 1.0 / 4, ['¾'] = 3.0 / 4,
        ['⅛'] = 1.0 / 8, ['⅜'] = 3.0 / 8, ['⅝'] = 5.0 / 8, ['⅞'] = 7.0 / 8
    };

    private sealed record UnitInfo(string Canonical, bool Metric, bool Volume);

    private static readonly Dictionary<string, UnitInfo> Units = BuildUnits();

    private static Dictionary<string, UnitInfo> BuildUnits()
    {
        var map = new Dictionary<string, UnitInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(string canonical, bool metric, bool volume, params string[] names)
        {
            foreach (var name in names)
            {
                map[name] = new UnitInfo(canonical, metric, volume);
            }
        }

        Add("cup", false, true, "cup", "cups", "c");
        Add("tablespoon", false, true, "tablespoon", "tablespoons", "tbsp", "tbsp.", "tbs", "tbs.", "tbl");
        Add("teaspoon", false, true, "teaspoon", "teaspoons", "tsp", "tsp.");
        Add("fluid ounce", false, true, "fl", "fluid");
        Add("pint", false, true, "pint", "pints", "pt");
        Add("quart", false, true, "quart", "quarts", "qt");
        Add("gallon", false, true, "gallon", "gallons", "gal");
        Add("ounce", false, false, "ounce", "ounces", "oz", "oz.");
        Add("pound", false, false, "pound", "pounds", "lb", "lbs", "lb.", "lbs.");
        Add("milliliter", true, true, "ml", "milliliter", "milliliters", "millilitre", "millilitres");
        Add("liter", true, true, "l", "liter", "liters", "litre", "litres");
        Add("gram", true, false, "g", "gram", "grams");
        Add("kilogram", true, false, "kg", "kilogram", "kilograms");
        return map;
    }

    public static string Scale(string ingredient, double multiplier)
    {
        if (multiplier <= 0 || !double.IsFinite(multiplier))
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }

        if (!TryParse(ingredient, out var parsed))
        {
            return ingredient;
        }

        return parsed.Lead + FormatRange(parsed.First * multiplier, parsed.Second * multiplier) + parsed.Rest;
    }

    /// <summary>Converts a US ingredient line to metric (or metric to US). Unrecognized lines are returned unchanged.</summary>
    public static string Convert(string ingredient, bool toMetric)
    {
        if (!TryParse(ingredient, out var parsed) || !TrySplitUnit(parsed.Rest, out var unit, out var after)
            || !Units.TryGetValue(unit.Trim(), out var info) || info.Metric == toMetric)
        {
            return ingredient;
        }

        // "fl oz" / "fluid ounces" are two words.
        var canonical = info.Canonical;
        if (canonical == "fluid ounce")
        {
            var words = after.TrimStart().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || !words[0].StartsWith("oz", StringComparison.OrdinalIgnoreCase)
                && !words[0].StartsWith("ounce", StringComparison.OrdinalIgnoreCase))
            {
                return ingredient;
            }

            after = words.Length > 1 ? " " + words[1] : "";
        }

        var firstText = ConvertAmount(parsed.First, canonical, toMetric, out var firstUnit);
        if (firstText is null)
        {
            return ingredient;
        }

        var amount = firstText;
        if (parsed.Second is { } second)
        {
            var secondText = ConvertAmount(second, canonical, toMetric, out var secondUnit);
            if (secondText is null || secondUnit != firstUnit)
            {
                return ingredient;
            }

            amount = $"{firstText}-{secondText}";
        }

        return $"{parsed.Lead}{amount} {firstUnit}{after}";
    }

    public static string ConvertTemperatures(string text, bool toMetric) => toMetric
        ? Fahrenheit.Replace(text, match => $"{Math.Round((double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture) - 32) * 5 / 9 / 5) * 5:0}°C")
        : Celsius.Replace(text, match => $"{Math.Round((double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture) * 9 / 5 + 32) / 5) * 5:0}°F");

    public static string FormatAmount(double value)
    {
        if (value <= 0)
        {
            return "0";
        }

        var whole = Math.Floor(value);
        var fraction = value - whole;
        (double Value, string Text)[] steps =
        [
            (0, ""), (1.0 / 8, "1/8"), (1.0 / 4, "1/4"), (1.0 / 3, "1/3"), (3.0 / 8, "3/8"), (1.0 / 2, "1/2"),
            (5.0 / 8, "5/8"), (2.0 / 3, "2/3"), (3.0 / 4, "3/4"), (7.0 / 8, "7/8"), (1, "")
        ];
        var best = steps.MinBy(step => Math.Abs(step.Value - fraction));
        if (value >= 0.12 && Math.Abs(best.Value - fraction) < 0.03)
        {
            if (best.Value == 1)
            {
                whole += 1;
            }

            if (best.Text.Length == 0)
            {
                return whole.ToString("0", CultureInfo.InvariantCulture);
            }

            return whole == 0 ? best.Text : $"{whole:0} {best.Text}";
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Splits an ingredient into a combinable key and amount (used by the grocery list).</summary>
    public static bool TryReadForTotals(string ingredient, out double amount, out string unit, out string name)
    {
        amount = 0;
        unit = "";
        name = ingredient.Trim();
        if (!TryParse(ingredient, out var parsed))
        {
            return false;
        }

        amount = parsed.Second is { } second ? (parsed.First + second) / 2 : parsed.First;
        var rest = parsed.Rest;
        if (TrySplitUnit(rest, out var token, out var after) && Units.TryGetValue(token.Trim(), out var info)
            && info.Canonical != "fluid ounce")
        {
            unit = info.Canonical;
            rest = after;
        }

        name = rest.Trim();
        return name.Length > 0;
    }

    public static string FormatWithUnit(double amount, string unit, string name)
    {
        var number = FormatAmount(amount);
        var unitText = unit.Length == 0 ? "" : " " + (amount > 1.0001 ? Plural(unit) : unit);
        return $"{number}{unitText} {name}".Trim();
    }

    private static string Plural(string unit) => unit switch
    {
        "ounce" or "pound" or "cup" or "tablespoon" or "teaspoon" or "pint" or "quart" or "gallon" or "gram" or "kilogram"
            or "milliliter" or "liter" => unit + "s",
        _ => unit
    };

    private static string? ConvertAmount(double amount, string canonical, bool toMetric, out string unit)
    {
        unit = "";
        if (toMetric)
        {
            double? millilitres = canonical switch
            {
                "cup" => amount * 240,
                "tablespoon" => amount * 15,
                "teaspoon" => amount * 5,
                "fluid ounce" => amount * 30,
                "pint" => amount * 480,
                "quart" => amount * 960,
                "gallon" => amount * 3800,
                _ => null
            };
            if (millilitres is { } ml)
            {
                if (ml >= 1000)
                {
                    unit = "L";
                    return (ml / 1000).ToString("0.##", CultureInfo.InvariantCulture);
                }

                unit = "ml";
                return RoundMeasure(ml);
            }

            double? grams = canonical switch { "ounce" => amount * 28.35, "pound" => amount * 453.6, _ => null };
            if (grams is { } g)
            {
                if (g >= 1000)
                {
                    unit = "kg";
                    return (g / 1000).ToString("0.##", CultureInfo.InvariantCulture);
                }

                unit = "g";
                return RoundMeasure(g);
            }

            return null;
        }

        switch (canonical)
        {
            case "milliliter" or "liter":
                var ml = canonical == "liter" ? amount * 1000 : amount;
                if (ml < 15)
                {
                    unit = "tsp";
                    return FormatAmount(ml / 5);
                }

                if (ml < 60)
                {
                    unit = "tbsp";
                    return FormatAmount(ml / 15);
                }

                unit = "cups";
                return FormatAmount(ml / 240);
            case "gram" or "kilogram":
                var g = canonical == "kilogram" ? amount * 1000 : amount;
                if (g < 450)
                {
                    unit = "oz";
                    return FormatAmount(g / 28.35);
                }

                unit = "lb";
                return FormatAmount(g / 453.6);
            default:
                return null;
        }
    }

    private static string RoundMeasure(double value)
    {
        var rounded = value switch
        {
            < 20 => Math.Round(value * 2) / 2,
            < 100 => Math.Round(value),
            _ => Math.Round(value / 5) * 5
        };
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool TrySplitUnit(string rest, out string unit, out string after)
    {
        var match = Regex.Match(rest, @"^\s*(?<unit>[A-Za-z]+\.?)(?<after>.*)$", RegexOptions.Singleline);
        unit = match.Success ? match.Groups["unit"].Value : "";
        after = match.Success ? match.Groups["after"].Value : "";
        return match.Success;
    }

    private static string FormatRange(double first, double? second) => second is { } value
        ? $"{FormatAmount(first)}-{FormatAmount(value)}"
        : FormatAmount(first);

    private readonly record struct Parsed(string Lead, double First, double? Second, string Rest);

    private static bool TryParse(string ingredient, out Parsed parsed)
    {
        parsed = default;
        var match = Leading.Match(ingredient);
        if (!match.Success || !TryParseNumber(match.Groups["first"].Value, out var first) || first <= 0)
        {
            return false;
        }

        double? second = null;
        if (match.Groups["second"].Success)
        {
            if (!TryParseNumber(match.Groups["second"].Value, out var parsedSecond))
            {
                return false;
            }

            second = parsedSecond;
            // "1-1/2 cups" means one and a half, not a range.
            if (parsedSecond < first && match.Groups["second"].Value.Contains('/')
                && !match.Groups["first"].Value.Contains('/') && !match.Groups["first"].Value.Contains(' '))
            {
                first += parsedSecond;
                second = null;
            }
        }

        parsed = new Parsed(match.Groups["lead"].Value, first, second, match.Groups["rest"].Value);
        return true;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        value = 0;
        text = text.Trim();
        var last = text[^1];
        if (FractionValues.TryGetValue(last, out var glyph))
        {
            var wholeText = text[..^1].Trim();
            if (wholeText.Length == 0)
            {
                value = glyph;
                return true;
            }

            if (!double.TryParse(wholeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var whole))
            {
                return false;
            }

            value = whole + glyph;
            return true;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && TryParseFraction(parts[1], out var fraction)
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var mixed))
        {
            value = mixed + fraction;
            return true;
        }

        if (parts.Length == 1 && TryParseFraction(parts[0], out fraction))
        {
            value = fraction;
            return true;
        }

        return parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseFraction(string text, out double result)
    {
        result = 0;
        var parts = text.Split('/');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            || denominator == 0)
        {
            return false;
        }

        result = numerator / denominator;
        return true;
    }
}
