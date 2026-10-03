using System.Windows;

namespace RecipeTool.App;

public partial class CategoryPickerWindow : Window
{
    public string SelectedCategory { get; private set; }

    public CategoryPickerWindow(string recipeName, IEnumerable<string> categories, string suggested)
    {
        InitializeComponent();
        RecipeNameText.Text = recipeName;
        var list = categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!list.Contains(suggested, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(suggested);
        }

        CategoryBox.ItemsSource = list;
        CategoryBox.SelectedItem = list.First(category => string.Equals(category, suggested, StringComparison.OrdinalIgnoreCase));
        SelectedCategory = (string)CategoryBox.SelectedItem;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SelectedCategory = CategoryBox.SelectedItem as string ?? SelectedCategory;
        DialogResult = true;
    }
}
