namespace RecipeTool.Core;

public static class RecipeService
{
    public const string FavoritesCategory = "♥ Favorites";
    public const string TrashCategory = "🗑 Trash";
    public const int TrashDays = 30;

    public static readonly string[] StarterCategories =
    [
        "Appetizers & Snacks",
        "Beef",
        "Breads & Breakfast",
        "Cakes",
        "Casseroles",
        "Chicken & Turkey",
        "Cookies",
        "Desserts",
        "Drinks",
        "Fish & Seafood",
        "Holiday & Family Favorites",
        "Pasta",
        "Pies & Tarts",
        "Pork",
        "Salads",
        "Sauces & Condiments",
        "Soups & Stews",
        "Vegetables & Sides",
        "Vegetarian",
        "Other"
    ];

    private static readonly (string Keyword, string Category)[] CategoryKeywords =
    [
        ("cookie", "Cookies"), ("brownie", "Cookies"), ("cupcake", "Cakes"), ("cake", "Cakes"),
        ("pie", "Pies & Tarts"), ("tart", "Pies & Tarts"), ("pasta", "Pasta"), ("spaghetti", "Pasta"),
        ("lasagna", "Pasta"), ("macaroni", "Pasta"), ("beef", "Beef"), ("steak", "Beef"), ("meatloaf", "Beef"),
        ("chicken", "Chicken & Turkey"), ("turkey", "Chicken & Turkey"), ("pork", "Pork"), ("bacon", "Pork"),
        ("salmon", "Fish & Seafood"), ("shrimp", "Fish & Seafood"), ("fish", "Fish & Seafood"),
        ("soup", "Soups & Stews"), ("stew", "Soups & Stews"), ("chili", "Soups & Stews"),
        ("salad", "Salads"), ("casserole", "Casseroles"), ("bread", "Breads & Breakfast"),
        ("pancake", "Breads & Breakfast"), ("muffin", "Breads & Breakfast"), ("sauce", "Sauces & Condiments"),
        ("dip", "Appetizers & Snacks"), ("smoothie", "Drinks"), ("cocktail", "Drinks"), ("lemonade", "Drinks")
    ];

    public static string SuggestCategory(string? name, string? siteCategory)
    {
        var text = $"{name} {siteCategory}";
        foreach (var (keyword, category) in CategoryKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        var site = StarterCategories.FirstOrDefault(c => string.Equals(c, siteCategory?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (site is not null)
        {
            return site;
        }

        return text.Contains("dessert", StringComparison.OrdinalIgnoreCase) ? "Desserts" : "Other";
    }

    public static IReadOnlyList<Recipe> Search(IEnumerable<Recipe> recipes, string query, string? category = null)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var trash = category == TrashCategory;
        return recipes
            .Where(recipe => recipe.IsDeleted == trash)
            .Where(recipe => string.IsNullOrWhiteSpace(category)
                || category == "All recipes"
                || trash
                || (category == FavoritesCategory
                    ? recipe.IsFavorite
                    : string.Equals(recipe.Category, category, StringComparison.OrdinalIgnoreCase)))
            .Where(recipe => terms.Length == 0 || terms.All(term => Contains(recipe, term)))
            .OrderByDescending(recipe => recipe.UpdatedAt)
            .ToList();
    }

    public static Recipe CreateAdjustedCopy(Recipe original, int newServings, string versionName)
    {
        if (newServings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newServings), "Servings must be greater than zero.");
        }

        if (original.Servings <= 0)
        {
            throw new InvalidDataException("The original recipe has an invalid serving count.");
        }

        var multiplier = (double)newServings / original.Servings;
        return new Recipe
        {
            Name = original.Name,
            Description = original.Description,
            Category = original.Category,
            Tags = [.. original.Tags],
            Servings = newServings,
            Ingredients = original.Ingredients.Select(line => ScaleIngredient(line, multiplier)).ToList(),
            Instructions = [.. original.Instructions],
            Notes = string.IsNullOrWhiteSpace(original.Notes)
                ? $"Adjusted version: {versionName}"
                : $"{original.Notes}{Environment.NewLine}{Environment.NewLine}Adjusted version: {versionName}",
            SourceUrl = original.SourceUrl,
            ParentRecipeId = original.Id,
            VersionName = versionName.Trim(),
            NeedsReview = original.NeedsReview
        };
    }

    public static string ScaleIngredient(string ingredient, double multiplier) => IngredientMath.Scale(ingredient, multiplier);
    private static bool Contains(Recipe recipe, string term)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        return recipe.Name.Contains(term, comparison)
            || recipe.Description.Contains(term, comparison)
            || recipe.Category.Contains(term, comparison)
            || recipe.Notes.Contains(term, comparison)
            || recipe.SourceUrl.Contains(term, comparison)
            || recipe.Tags.Any(tag => tag.Contains(term, comparison))
            || recipe.Ingredients.Any(line => line.Contains(term, comparison))
            || recipe.Instructions.Any(line => line.Contains(term, comparison));
    }

    public static Recipe? FindDuplicate(IEnumerable<Recipe> recipes, Recipe incoming)
    {
        var url = NormalizeUrl(incoming.SourceUrl);
        return recipes.Where(r => !r.IsDeleted).FirstOrDefault(r =>
            (url.Length > 0 && NormalizeUrl(r.SourceUrl) == url && r.ParentRecipeId is null)
            || (r.ParentRecipeId is null && incoming.ParentRecipeId is null
                && string.Equals(r.Name.Trim(), incoming.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return "";
        }

        return (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host).ToLowerInvariant()
            + uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
    }

    public static int PurgeTrash(List<Recipe> recipes, DateTime nowUtc)
        => recipes.RemoveAll(r => r.DeletedAt is { } d && (nowUtc - d).TotalDays > TrashDays);
}
