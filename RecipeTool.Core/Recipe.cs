namespace RecipeTool.Core;

public sealed class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Other";
    public List<string> Tags { get; set; } = [];
    public int Servings { get; set; } = 4;
    public List<string> Ingredients { get; set; } = [];
    public List<string> Instructions { get; set; } = [];
    public string Notes { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ParentRecipeId { get; set; }
    public string? VersionName { get; set; }
    public bool NeedsReview { get; set; }
    public bool IsFavorite { get; set; }
    public int Rating { get; set; }
    public DateTime? LastMade { get; set; }
    public DateTime? DeletedAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDeleted => DeletedAt.HasValue;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Badges => (IsFavorite ? "♥ " : "") + (Rating > 0 ? new string('★', Math.Clamp(Rating, 1, 5)) : "");

    public Recipe Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Category = Category,
        Tags = [.. Tags],
        Servings = Servings,
        Ingredients = [.. Ingredients],
        Instructions = [.. Instructions],
        Notes = Notes,
        SourceUrl = SourceUrl,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        ParentRecipeId = ParentRecipeId,
        VersionName = VersionName,
        NeedsReview = NeedsReview,
        IsFavorite = IsFavorite,
        Rating = Rating,
        LastMade = LastMade,
        DeletedAt = DeletedAt
    };
}
