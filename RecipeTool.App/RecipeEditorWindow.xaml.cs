using RecipeTool.Core;
using System.Windows;

namespace RecipeTool.App;

public partial class RecipeEditorWindow : Window
{
    private readonly Recipe _draft;
    public Recipe? ResultRecipe { get; private set; }

    public RecipeEditorWindow(Recipe recipe, IEnumerable<string> categories)
    {
        InitializeComponent();
        _draft = recipe;
        EditorHeading.Text = recipe.NeedsReview ? "Review captured recipe" : recipe.ParentRecipeId.HasValue ? "Edit adjusted version" : "Recipe details";
        CategoryBox.ItemsSource = RecipeService.StarterCategories
            .Concat(categories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();
        NameBox.Text = recipe.Name;
        CategoryBox.Text = recipe.Category;
        ServingsBox.Text = recipe.Servings.ToString();
        DescriptionBox.Text = recipe.Description;
        TagsBox.Text = string.Join(", ", recipe.Tags);
        IngredientsBox.Text = string.Join(Environment.NewLine, recipe.Ingredients);
        InstructionsBox.Text = string.Join(Environment.NewLine, recipe.Instructions);
        NotesBox.Text = recipe.Notes;
        SourceBox.Text = recipe.SourceUrl;
        ReviewCheckBox.IsChecked = recipe.NeedsReview;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("Please enter a recipe name.", "Recipe name required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }

        if (!int.TryParse(ServingsBox.Text, out var servings) || servings < 1 || servings > 10000)
        {
            MessageBox.Show("Enter a serving count between 1 and 10,000.", "Check servings",
                MessageBoxButton.OK, MessageBoxImage.Information);
            ServingsBox.Focus();
            return;
        }

        var category = CategoryBox.Text.Trim();
        if (category.Length == 0)
        {
            category = "Other";
        }

        _draft.Name = name;
        _draft.Category = category;
        _draft.Servings = servings;
        _draft.Description = DescriptionBox.Text.Trim();
        _draft.Tags = TagsBox.Text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _draft.Ingredients = SplitLines(IngredientsBox.Text);
        _draft.Instructions = SplitLines(InstructionsBox.Text);
        _draft.Notes = NotesBox.Text.Trim();
        _draft.SourceUrl = SourceBox.Text.Trim();
        _draft.NeedsReview = ReviewCheckBox.IsChecked == true;
        _draft.UpdatedAt = DateTime.UtcNow;
        ResultRecipe = _draft;
        DialogResult = true;
    }

    private static List<string> SplitLines(string value) => value
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
}
