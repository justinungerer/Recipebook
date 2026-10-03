using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecipeTool.Core;

public sealed class RecipeLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<Recipe> Recipes { get; private set; } = [];
    public string FilePath { get; }

    public RecipeLibrary(string filePath)
    {
        FilePath = filePath;
    }

    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            Recipes = [];
            return;
        }

        var json = File.ReadAllText(FilePath);
        Recipes = JsonSerializer.Deserialize<List<Recipe>>(json, JsonOptions)
            ?? throw new InvalidDataException("The recipe library file did not contain a recipe list.");
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The recipe library must have a valid folder path.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Recipes, JsonOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public static List<Recipe> ReadBackup(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<Recipe>>(json, JsonOptions)
            ?? throw new InvalidDataException("The backup file did not contain a recipe list.");
    }

    public static void WriteBackup(string filePath, IEnumerable<Recipe> recipes)
    {
        var json = JsonSerializer.Serialize(recipes, JsonOptions);
        File.WriteAllText(filePath, json);
    }
}
