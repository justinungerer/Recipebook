using RecipeTool.Core;
using RecipeTool.App;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

var checks = new (string Name, Action Run)[]
{
    ("starter categories include the requested recipe types", CategoriesCoverCommonRecipes),
    ("category suggestion maps captured recipes to starter categories", () =>
    {
        Assert(RecipeService.SuggestCategory("Best Chocolate Chip Cookies", "Dessert") == "Cookies", "Cookies not suggested.");
        Assert(RecipeService.SuggestCategory("Lemon Pie", null) == "Pies & Tarts", "Pie not suggested.");
        Assert(RecipeService.SuggestCategory("Mystery", "Dessert") == "Desserts", "Dessert not suggested.");
        Assert(RecipeService.SuggestCategory("Mystery", "") == "Other", "Fallback should be Other.");
    }),
    ("search finds every term in recipe fields without case sensitivity", SearchMatchesRecipeFields),
    ("category filters and search combine", CategoryAndSearchCombine),
    ("adjusted copy preserves original and scales amounts", AdjustedCopyScalesIngredients),
    ("unrecognized ingredient amounts remain unchanged", UnrecognizedAmountIsPreserved),
    ("invalid serving counts are rejected", InvalidServingsAreRejected),
    ("recipe library saves and reloads a recipe", LibraryRoundTrips),
    ("invalid backup is reported instead of treated as empty", InvalidBackupIsReported),
    ("scaling handles unicode fractions, ranges, mixed numbers and nice fractions", () =>
    {
        Assert(IngredientMath.Scale("1½ cups sugar", 2) == "3 cups sugar", IngredientMath.Scale("1½ cups sugar", 2));
        Assert(IngredientMath.Scale("1-2 cloves garlic", 2) == "2-4 cloves garlic", IngredientMath.Scale("1-2 cloves garlic", 2));
        Assert(IngredientMath.Scale("1 cup flour", 1.5) == "1 1/2 cup flour", IngredientMath.Scale("1 cup flour", 1.5));
        Assert(IngredientMath.Scale("1 cup flour", 1.0 / 3) == "1/3 cup flour", IngredientMath.Scale("1 cup flour", 1.0 / 3));
        Assert(IngredientMath.Scale("1-1/2 cups milk", 2) == "3 cups milk", IngredientMath.Scale("1-1/2 cups milk", 2));
        Assert(IngredientMath.Scale("12-inch pan", 2) == "12-inch pan", "12-inch should not scale");
        Assert(IngredientMath.Scale("2 eggs", 2) == "4 eggs", "eggs");
    }),
    ("unit conversion works both ways and converts oven temperatures", () =>
    {
        Assert(IngredientMath.Convert("1 cup milk", true) == "240 ml milk", IngredientMath.Convert("1 cup milk", true));
        Assert(IngredientMath.Convert("2 lbs beef", true) == "905 g beef", IngredientMath.Convert("2 lbs beef", true));
        Assert(IngredientMath.Convert("240 ml milk", false) == "1 cups milk", IngredientMath.Convert("240 ml milk", false));
        Assert(IngredientMath.Convert("2 eggs", true) == "2 eggs", "eggs unchanged");
        Assert(IngredientMath.ConvertTemperatures("Bake at 350 degrees F.", true) == "Bake at 175°C.", IngredientMath.ConvertTemperatures("Bake at 350 degrees F.", true));
        Assert(IngredientMath.ConvertTemperatures("Bake at 350 degrees F (175 degrees C).", true).Contains("350 degrees F"), "already dual");
    }),
    ("grocery list adds up matching items", () =>
    {
        var list = GroceryList.Combine([["2 cups flour", "1 egg", "salt to taste"], ["1 1/2 cups flour", "2 eggs", "salt to taste"]]);
        Assert(list.Contains("3 1/2 cups flour"), string.Join("|", list));
        Assert(list.Contains("3 egg") || list.Contains("3 eggs"), string.Join("|", list));
        Assert(list.Count(l => l == "salt to taste") == 1, string.Join("|", list));
    }),
    ("duplicates are found by URL or name; trash hides and purges", () =>
    {
        var a = new Recipe { Name = "Cookies", SourceUrl = "https://www.site.com/r/1/cookies/" };
        var list = new List<Recipe> { a };
        Assert(RecipeService.FindDuplicate(list, new Recipe { Name = "x", SourceUrl = "http://site.com/r/1/cookies" }) == a, "url");
        Assert(RecipeService.FindDuplicate(list, new Recipe { Name = " cookies " }) == a, "name");
        Assert(RecipeService.FindDuplicate(list, new Recipe { Name = "Pie" }) is null, "none");
        a.IsFavorite = true;
        Assert(RecipeService.Search(list, "", RecipeService.FavoritesCategory).Count == 1, "fav");
        a.DeletedAt = DateTime.UtcNow.AddDays(-40);
        Assert(RecipeService.Search(list, "").Count == 0, "hidden");
        Assert(RecipeService.Search(list, "", RecipeService.TrashCategory).Count == 1, "trash view");
        Assert(RecipeService.FindDuplicate(list, new Recipe { Name = "Cookies" }) is null, "deleted ignored");
        Assert(RecipeService.PurgeTrash(list, DateTime.UtcNow) == 1 && list.Count == 0, "purge");
    }),
    ("pantry ranks recipes and meal plan round trips", () =>
    {
        var pancakes = new Recipe { Name = "Pancakes", Ingredients = ["1 cup flour", "1 egg", "1 cup milk", "salt"] };
        var omelet = new Recipe { Name = "Omelet", Ingredients = ["3 eggs", "1/4 cup cheese"] };
        var ranked = Pantry.Rank([pancakes, omelet], "flour, eggs, milk", true);
        Assert(ranked[0].Recipe == pancakes && ranked[0].Missing.Count == 0, "pancakes first");
        Assert(ranked[1].Missing.Count == 1, "omelet missing cheese");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var plan = new MealPlan(path);
        plan.Entries.Add(new MealPlanEntry { Date = new DateTime(2025, 1, 8), Slot = "Dinner", RecipeId = pancakes.Id });
        plan.Save();
        var loaded = new MealPlan(path);
        loaded.Load();
        File.Delete(path);
        Assert(loaded.Entries.Count == 1 && loaded.ForDay(new DateTime(2025, 1, 8)).Count() == 1, "plan");
        Assert(MealPlan.StartOfWeek(new DateTime(2025, 1, 8)) == new DateTime(2025, 1, 5), "sunday");
    })
};

var failures = new List<string>();
foreach (var (name, run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
        Console.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

try
{
    await CaptureBridgeRoundTripsAsync();
    Console.WriteLine("PASS capture bridge authenticates, imports, and rejects duplicate requests");
}
catch (Exception exception)
{
    failures.Add($"capture bridge integration: {exception.Message}");
    Console.WriteLine($"FAIL capture bridge integration: {exception.Message}");
}

Console.WriteLine($"{checks.Length + 1 - failures.Count}/{checks.Length + 1} checks passed.");
if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

static void CategoriesCoverCommonRecipes()
{
    foreach (var category in new[] { "Cakes", "Cookies", "Pies & Tarts", "Beef", "Pasta" })
    {
        Assert(RecipeService.StarterCategories.Contains(category), $"Missing category {category}.");
    }
}

static void SearchMatchesRecipeFields()
{
    var recipe = MakeRecipe();
    recipe.Ingredients = ["2 cups brown sugar"];
    recipe.Tags = ["holiday favorite"];
    recipe.Instructions = ["Bake until golden."];
    Assert(RecipeService.Search([recipe], "brown holiday").Count == 1, "Search did not match terms across fields.");
    Assert(RecipeService.Search([recipe], "missing").Count == 0, "Search returned a non-matching recipe.");
}

static void CategoryAndSearchCombine()
{
    var cake = MakeRecipe();
    cake.Category = "Cakes";
    cake.Name = "Apple cake";
    var pie = MakeRecipe();
    pie.Category = "Pies & Tarts";
    pie.Name = "Apple pie";
    var result = RecipeService.Search([cake, pie], "apple", "Cakes");
    Assert(result.Count == 1 && result[0].Id == cake.Id, "Category and text filters did not combine.");
}

static void AdjustedCopyScalesIngredients()
{
    var original = MakeRecipe();
    original.Servings = 4;
    original.Ingredients = ["2 1/2 cups flour", "1/2 teaspoon salt", "1.5 cups milk", "salt to taste"];
    var copy = RecipeService.CreateAdjustedCopy(original, 8, "Double batch");
    Assert(original.Servings == 4, "The original serving count changed.");
    Assert(copy.ParentRecipeId == original.Id, "The new recipe was not linked to its parent.");
    Assert(copy.VersionName == "Double batch", "The version name was not saved.");
    Assert(copy.Ingredients.SequenceEqual(["5 cups flour", "1 teaspoon salt", "3 cups milk", "salt to taste"]),
        "Ingredient quantities were not scaled as expected.");
}

static void UnrecognizedAmountIsPreserved()
{
    Assert(RecipeService.ScaleIngredient("a pinch of cinnamon", 2) == "a pinch of cinnamon",
        "An ingredient without a recognized quantity was altered.");
}

static void InvalidServingsAreRejected()
{
    var threw = false;
    try
    {
        RecipeService.CreateAdjustedCopy(MakeRecipe(), 0, "Invalid");
    }
    catch (ArgumentOutOfRangeException)
    {
        threw = true;
    }
    Assert(threw, "A non-positive serving count was accepted.");
}

static void LibraryRoundTrips()
{
    WithTemporaryFolder(folder =>
    {
        var library = new RecipeLibrary(Path.Combine(folder, "recipes.json"));
        library.Recipes.Add(MakeRecipe());
        library.Save();
        var loaded = new RecipeLibrary(library.FilePath);
        loaded.Load();
        Assert(loaded.Recipes.Count == 1, "The saved recipe was not reloaded.");
        Assert(loaded.Recipes[0].Name == "Apple cake", "The recipe name did not survive serialization.");
        Assert(loaded.Recipes[0].Id == library.Recipes[0].Id, "The recipe identity did not survive serialization.");
    });
}

static void InvalidBackupIsReported()
{
    WithTemporaryFolder(folder =>
    {
        var path = Path.Combine(folder, "broken.json");
        File.WriteAllText(path, "{not json");
        var threw = false;
        try
        {
            RecipeLibrary.ReadBackup(path);
        }
        catch (System.Text.Json.JsonException)
        {
            threw = true;
        }
        Assert(threw, "A malformed backup was accepted.");
    });
}

static async Task CaptureBridgeRoundTripsAsync()
{
    const string token = "cba61e4a-f2c9-4b40-a691-3fd428d775b6";
    const string origin = "chrome-extension://abcdefghijklmnopabcdefghijklmnop";
    var received = new TaskCompletionSource<CapturedRecipe>(TaskCreationOptions.RunContinuationsAsynchronously);
    var server = new RecipeCaptureServer(recipe =>
    {
        received.TrySetResult(recipe);
        return Task.CompletedTask;
    });

    await server.StartAsync();
    try
    {
        using var client = new HttpClient();
        using var invalidRequest = MakeRequest(HttpMethod.Get, "api/status", origin, "incorrect-token");
        using var invalidResponse = await client.SendAsync(invalidRequest);
        Assert(invalidResponse.StatusCode == HttpStatusCode.Unauthorized, "An incorrect bridge token was accepted.");

        using var statusRequest = MakeRequest(HttpMethod.Get, "api/status", origin, token);
        using var statusResponse = await client.SendAsync(statusRequest);
        Assert(statusResponse.StatusCode == HttpStatusCode.OK, "An authorized extension was not accepted.");

        using var noOriginRequest = MakeRequest(HttpMethod.Get, "api/status", null, token);
        using var noOriginResponse = await client.SendAsync(noOriginRequest);
        Assert(noOriginResponse.StatusCode == HttpStatusCode.OK, "A tokened request without an Origin header was rejected (Chrome omits it).");

        using var webPageRequest = MakeRequest(HttpMethod.Get, "api/status", "https://evil.example", token);
        using var webPageResponse = await client.SendAsync(webPageRequest);
        Assert(webPageResponse.StatusCode == HttpStatusCode.Unauthorized, "A web page origin was accepted.");

        server.RequestCapture();
        using var pollRequest = MakeRequest(HttpMethod.Get, "api/capture-request", origin, token);
        using var pollResponse = await client.SendAsync(pollRequest);
        Assert(pollResponse.StatusCode == HttpStatusCode.OK, "A queued capture was not offered to Chrome.");
        using var requestJson = JsonDocument.Parse(await pollResponse.Content.ReadAsStringAsync());
        var requestId = requestJson.RootElement.GetProperty("requestId").GetGuid();

        var capture = new CapturedRecipe
        {
            Name = "Captured pancake recipe",
            Category = "Breads & Breakfast",
            Ingredients = ["1 cup flour"],
            Instructions = ["Mix and cook."],
            SourceUrl = "https://example.com/pancakes"
        };
        using var postRequest = MakeRequest(HttpMethod.Post, $"api/capture/{requestId}", origin, token);
        postRequest.Content = new StringContent(JsonSerializer.Serialize(capture), Encoding.UTF8, "application/json");
        using var postResponse = await client.SendAsync(postRequest);
        Assert(postResponse.StatusCode == HttpStatusCode.Accepted, "The app did not accept the captured recipe.");

        var saved = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert(saved.Name == capture.Name && saved.SourceUrl == capture.SourceUrl,
            "Captured recipe details did not reach the desktop app.");
        using var duplicateRequest = MakeRequest(HttpMethod.Post, $"api/capture/{requestId}", origin, token);
        duplicateRequest.Content = new StringContent(JsonSerializer.Serialize(capture), Encoding.UTF8, "application/json");
        using var duplicateResponse = await client.SendAsync(duplicateRequest);
        Assert(duplicateResponse.StatusCode == HttpStatusCode.Conflict, "The capture request was accepted more than once.");
    }
    finally
    {
        await server.StopAsync();
    }
}

static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? origin, string token)
{
    var request = new HttpRequestMessage(method, $"http://127.0.0.1:47831/{path}");
    if (origin is not null)
    {
        request.Headers.TryAddWithoutValidation("Origin", origin);
    }
    request.Headers.TryAddWithoutValidation("X-RecipeTool-Token", token);
    return request;
}

static Recipe MakeRecipe() => new()
{
    Name = "Apple cake",
    Category = "Other",
    Servings = 4
};

static void WithTemporaryFolder(Action<string> action)
{
    var folder = Path.Combine(Path.GetTempPath(), $"recipe-tool-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try
    {
        action(folder);
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
