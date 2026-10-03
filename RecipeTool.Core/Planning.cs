using System.Text.Json;

namespace RecipeTool.Core;

public sealed class MealPlanEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public string Slot { get; set; } = "Dinner";
    public Guid RecipeId { get; set; }
}

public sealed class MealPlan
{
    public static readonly string[] Slots = ["Breakfast", "Lunch", "Dinner", "Snack"];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }
    public List<MealPlanEntry> Entries { get; private set; } = [];

    public MealPlan(string filePath) => FilePath = filePath;

    public void Load()
    {
        Entries = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<List<MealPlanEntry>>(File.ReadAllText(FilePath), JsonOptions) ?? []
            : [];
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Entries, JsonOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public static DateTime StartOfWeek(DateTime date)
        => date.Date.AddDays(-(int)date.DayOfWeek);

    public IEnumerable<MealPlanEntry> ForDay(DateTime day)
        => Entries.Where(e => e.Date.Date == day.Date)
            .OrderBy(e => Array.IndexOf(Slots, e.Slot));
}

public static class GroceryList
{
    /// <summary>Combines ingredient lines from several recipes, adding up matching items.</summary>
    public static List<string> Combine(IEnumerable<IEnumerable<string>> ingredientLists)
    {
        var totals = new Dictionary<string, (double Amount, string Unit, string Name)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var plain = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in ingredientLists.SelectMany(list => list))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!IngredientMath.TryReadForTotals(line, out var amount, out var unit, out var name))
            {
                plain.Add(line.Trim());
                continue;
            }

            var key = unit + "|" + Normalize(name);
            if (totals.TryGetValue(key, out var existing))
            {
                totals[key] = (existing.Amount + amount, unit, existing.Name);
            }
            else
            {
                totals[key] = (amount, unit, name);
                order.Add(key);
            }
        }

        var result = order.Select(key => IngredientMath.FormatWithUnit(totals[key].Amount, totals[key].Unit, totals[key].Name))
            .OrderBy(text => text, StringComparer.OrdinalIgnoreCase)
            .ToList();
        result.AddRange(plain.Where(p => !result.Contains(p, StringComparer.OrdinalIgnoreCase)));
        return result;
    }

    private static string Normalize(string name)
    {
        var text = name.Split(',')[0].Trim().ToLowerInvariant();
        return text.EndsWith("ies") ? text[..^3] + "y" : text.EndsWith('s') && !text.EndsWith("ss") ? text[..^1] : text;
    }
}

public sealed record PantryMatch(Recipe Recipe, int Matched, int Total, List<string> Missing)
{
    public double Percent => Total == 0 ? 0 : 100.0 * Matched / Total;
}

public static class Pantry
{
    private static readonly string[] Basics = ["salt", "pepper", "water", "oil", "ice"];

    public static List<PantryMatch> Rank(IEnumerable<Recipe> recipes, string onHand, bool assumeBasics)
    {
        var have = onHand.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Stem).Where(s => s.Length > 1).ToList();

        var results = new List<PantryMatch>();
        foreach (var recipe in recipes.Where(r => !r.IsDeleted && r.Ingredients.Count > 0))
        {
            var missing = new List<string>();
            var total = 0;
            var matched = 0;
            foreach (var line in recipe.Ingredients)
            {
                var text = line.ToLowerInvariant();
                if (assumeBasics && Basics.Any(b => text.Contains(b)))
                {
                    continue;
                }

                total++;
                if (have.Any(item => text.Contains(item)))
                {
                    matched++;
                }
                else
                {
                    missing.Add(line.Trim());
                }
            }

            if (have.Count > 0 && matched > 0 || have.Count == 0 && total == 0)
            {
                results.Add(new PantryMatch(recipe, matched, total, missing));
            }
        }

        return results.OrderByDescending(r => r.Percent).ThenBy(r => r.Missing.Count).ThenBy(r => r.Recipe.Name).ToList();
    }

    private static string Stem(string word)
    {
        var text = word.ToLowerInvariant();
        return text.EndsWith("ies") ? text[..^3] + "y" : text.EndsWith('s') && !text.EndsWith("ss") && text.Length > 3 ? text[..^1] : text;
    }
}
