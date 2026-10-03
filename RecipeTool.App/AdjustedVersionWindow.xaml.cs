using RecipeTool.Core;
using System.Windows;

namespace RecipeTool.App;

public partial class AdjustedVersionWindow : Window
{
    public string VersionName { get; private set; } = "";
    public int Servings { get; private set; }

    public AdjustedVersionWindow(Recipe original)
    {
        InitializeComponent();
        VersionNameBox.Text = "Adjusted";
        ServingsBox.Text = original.Servings.ToString();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        VersionName = VersionNameBox.Text.Trim();
        if (VersionName.Length == 0)
        {
            MessageBox.Show("Describe what is different in this version.", "Version name required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            VersionNameBox.Focus();
            return;
        }

        if (!int.TryParse(ServingsBox.Text, out var servings) || servings < 1 || servings > 10000)
        {
            MessageBox.Show("Enter a serving count between 1 and 10,000.", "Check servings",
                MessageBoxButton.OK, MessageBoxImage.Information);
            ServingsBox.Focus();
            return;
        }

        Servings = servings;
        DialogResult = true;
    }
}
