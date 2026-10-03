using Microsoft.Win32;
using RecipeTool.Core;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace RecipeTool.App;

internal static class UiKit
{
    public static Window Base(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = 520,
        MinHeight = 380,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("PaperBrush"),
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("InkBrush"),
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        FontSize = 14
    };

    public static TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };

    public static TextBlock Muted(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("MutedBrush")
    };
}

public sealed class GroceryListWindow : Window
{
    private readonly List<(CheckBox Box, Recipe Recipe)> _items = [];
    private readonly TextBox _output = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 15
    };

    public GroceryListWindow(IReadOnlyList<Recipe> recipes, IReadOnlyCollection<Guid> preselected)
    {
        Title = "Grocery list";
        Width = 820;
        Height = 620;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("PaperBrush");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 14;

        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var leftHeader = new StackPanel();
        leftHeader.Children.Add(UiKit.Heading("Pick recipes"));
        leftHeader.Children.Add(UiKit.Muted("Tick the recipes you're shopping for. Matching items are added together."));
        DockPanel.SetDock(leftHeader, Dock.Top);
        left.Children.Add(leftHeader);
        var checks = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var recipe in recipes)
        {
            var box = new CheckBox
            {
                Content = new TextBlock { Text = recipe.Name, TextWrapping = TextWrapping.Wrap },
                IsChecked = preselected.Contains(recipe.Id),
                Margin = new Thickness(0, 3, 0, 3)
            };
            box.Click += (_, _) => Rebuild();
            _items.Add((box, recipe));
            checks.Children.Add(box);
        }

        left.Children.Add(new ScrollViewer { Content = checks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        grid.Children.Add(left);

        var right = new DockPanel();
        Grid.SetColumn(right, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(MakeButton("Copy", (_, _) => CopyList()));
        buttons.Children.Add(MakeButton("Print", (_, _) => PrintList()));
        buttons.Children.Add(MakeButton("Save as file…", (_, _) => SaveList()));
        right.Children.Add(buttons);
        var rightHeader = UiKit.Heading("Shopping list");
        DockPanel.SetDock(rightHeader, Dock.Top);
        right.Children.Add(rightHeader);
        right.Children.Add(_output);
        grid.Children.Add(right);
        Content = grid;

        Rebuild();
    }

    private static Button MakeButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text };
        button.Click += click;
        return button;
    }

    private void Rebuild()
    {
        var chosen = _items.Where(item => item.Box.IsChecked == true).Select(item => item.Recipe).ToList();
        var lines = GroceryList.Combine(chosen.Select(recipe => (IEnumerable<string>)recipe.Ingredients));
        _output.Text = chosen.Count == 0
            ? "Tick a recipe on the left to build your list."
            : string.Join(Environment.NewLine, lines.Select(line => "☐ " + line));
    }

    private void CopyList()
    {
        try
        {
            Clipboard.SetText(_output.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, "The clipboard is busy. Please try again.", "Grocery list");
        }
    }

    private void SaveList()
    {
        var dialog = new SaveFileDialog { Filter = "Text file (*.txt)|*.txt", FileName = "Grocery list.txt" };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, _output.Text);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, exception.Message, "Could not save the list");
            }
        }
    }

    private void PrintList()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var document = new FlowDocument(new Paragraph(new Run(_output.Text)))
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(60),
            PageWidth = dialog.PrintableAreaWidth,
            PageHeight = dialog.PrintableAreaHeight,
            ColumnWidth = dialog.PrintableAreaWidth
        };
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Grocery list");
    }
}

public sealed class PantryWindow : Window
{
    private readonly RecipeLibrary _library;
    private readonly Action<Guid> _open;
    private readonly TextBox _input = new() { AcceptsReturn = false, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _basics = new() { Content = "Assume I have salt, pepper, water and oil", IsChecked = true, Margin = new Thickness(0, 8, 0, 8) };
    private readonly ListBox _results = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };

    public PantryWindow(RecipeLibrary library, Action<Guid> open)
    {
        _library = library;
        _open = open;
        Title = "What can I make?";
        Width = 640;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("PaperBrush");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 14;
        AutomationProperties.SetName(_input, "Ingredients on hand");

        var panel = new DockPanel { Margin = new Thickness(18) };
        var top = new StackPanel();
        top.Children.Add(UiKit.Heading("What can I make?"));
        top.Children.Add(UiKit.Muted("Type what you have on hand, separated by commas (for example: chicken, rice, onions)."));
        top.Children.Add(new Border { Height = 8 });
        top.Children.Add(_input);
        top.Children.Add(_basics);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        var hint = UiKit.Muted("Double-click a recipe to open it in your recipe book.");
        hint.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(hint, Dock.Bottom);
        panel.Children.Add(hint);
        panel.Children.Add(_results);
        Content = panel;

        _input.TextChanged += (_, _) => Refresh();
        _basics.Click += (_, _) => Refresh();
        _results.MouseDoubleClick += (_, _) =>
        {
            if (_results.SelectedItem is ListBoxItem { Tag: Guid id })
            {
                _open(id);
            }
        };
        Refresh();
    }

    private void Refresh()
    {
        _results.Items.Clear();
        if (string.IsNullOrWhiteSpace(_input.Text))
        {
            _results.Items.Add(new ListBoxItem { Content = "Start typing ingredients above…", IsEnabled = false });
            return;
        }

        var matches = Pantry.Rank(_library.Recipes, _input.Text, _basics.IsChecked == true);
        if (matches.Count == 0)
        {
            _results.Items.Add(new ListBoxItem { Content = "No saved recipes use those ingredients yet.", IsEnabled = false });
            return;
        }

        foreach (var match in matches)
        {
            var text = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
            text.Children.Add(new TextBlock
            {
                Text = $"{match.Recipe.Name}  —  {match.Matched} of {match.Total} ingredients",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(UiKit.Muted(match.Missing.Count == 0
                ? "✔ You have everything!"
                : "Missing: " + string.Join("; ", match.Missing.Take(6)) + (match.Missing.Count > 6 ? "…" : "")));
            _results.Items.Add(new ListBoxItem { Content = text, Tag = match.Recipe.Id });
        }
    }
}

public sealed class MealPlannerWindow : Window
{
    private readonly MealPlan _plan;
    private readonly RecipeLibrary _library;
    private readonly Action<Guid> _open;
    private readonly Grid _days = new();
    private readonly TextBlock _weekLabel = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    private DateTime _weekStart = MealPlan.StartOfWeek(DateTime.Today);

    public MealPlannerWindow(MealPlan plan, RecipeLibrary library, Action<Guid> open)
    {
        _plan = plan;
        _library = library;
        _open = open;
        Title = "Meal planner";
        Width = 1000;
        Height = 500;
        MinWidth = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("PaperBrush");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bar, Dock.Top);
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        nav.Children.Add(Nav("◀ Previous", () => _weekStart = _weekStart.AddDays(-7)));
        nav.Children.Add(Nav("This week", () => _weekStart = MealPlan.StartOfWeek(DateTime.Today)));
        nav.Children.Add(Nav("Next ▶", () => _weekStart = _weekStart.AddDays(7)));
        nav.Children.Add(_weekLabel);
        bar.Children.Add(nav);
        var grocery = new Button { Content = "🛒 Grocery list for this week", HorizontalAlignment = HorizontalAlignment.Right };
        grocery.Click += (_, _) => OpenGrocery();
        DockPanel.SetDock(grocery, Dock.Right);
        bar.Children.Insert(0, grocery);
        root.Children.Add(bar);
        root.Children.Add(_days);
        Content = root;
        Render();
    }

    private Button Nav(string text, Action change)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) =>
        {
            change();
            Render();
        };
        return button;
    }

    private IEnumerable<Recipe> Recipes => _library.Recipes.Where(r => !r.IsDeleted).OrderBy(r => r.Name);

    private void OpenGrocery()
    {
        var ids = _plan.Entries.Where(e => e.Date.Date >= _weekStart && e.Date.Date < _weekStart.AddDays(7))
            .Select(e => e.RecipeId).Distinct().ToList();
        new GroceryListWindow(Recipes.ToList(), ids) { Owner = this }.ShowDialog();
    }

    private void Render()
    {
        _weekLabel.Text = $"{_weekStart:MMM d} – {_weekStart.AddDays(6):MMM d, yyyy}";
        _days.Children.Clear();
        _days.ColumnDefinitions.Clear();
        for (var i = 0; i < 7; i++)
        {
            _days.ColumnDefinitions.Add(new ColumnDefinition());
            var day = _weekStart.AddDays(i);
            var card = new Border
            {
                Background = Brushes(day == DateTime.Today ? "#FFF1DD" : "White"),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(3),
                Padding = new Thickness(8)
            };
            var dock = new DockPanel();
            var header = new TextBlock { Text = day.ToString("ddd MMM d"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
            var add = new Button { Content = "+ Add meal", Margin = new Thickness(0, 6, 0, 0) };
            var captured = day;
            add.Click += (_, _) => AddMeal(captured);
            DockPanel.SetDock(add, Dock.Bottom);
            dock.Children.Add(add);

            var list = new StackPanel();
            foreach (var entry in _plan.ForDay(day).ToList())
            {
                var recipe = _library.Recipes.FirstOrDefault(r => r.Id == entry.RecipeId && !r.IsDeleted);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var remove = new Button { Content = "✕", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Remove from plan" };
                var current = entry;
                remove.Click += (_, _) =>
                {
                    _plan.Entries.Remove(current);
                    SavePlan();
                    Render();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap, Cursor = System.Windows.Input.Cursors.Hand };
                label.Inlines.Add(new Run(entry.Slot + "\n") { FontSize = 11, Foreground = Brushes("#766A64") });
                label.Inlines.Add(new Run(recipe?.Name ?? "(recipe removed)") { FontWeight = FontWeights.SemiBold });
                if (recipe is not null)
                {
                    label.MouseLeftButtonUp += (_, _) => _open(recipe.Id);
                    label.ToolTip = "Open this recipe";
                }

                row.Children.Add(label);
                list.Children.Add(row);
            }

            dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            card.Child = dock;
            Grid.SetColumn(card, i);
            _days.Children.Add(card);
        }
    }

    private static System.Windows.Media.Brush Brushes(string hex)
        => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;

    private void AddMeal(DateTime day)
    {
        var recipes = Recipes.ToList();
        if (recipes.Count == 0)
        {
            MessageBox.Show(this, "Add some recipes to your book first.", "Meal planner");
            return;
        }

        var dialog = UiKit.Base($"Add a meal for {day:dddd, MMM d}", 460, 250);
        dialog.Owner = this;
        dialog.ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(20) };
        var recipeBox = new ComboBox { DisplayMemberPath = nameof(Recipe.Name), ItemsSource = recipes, SelectedIndex = 0 };
        var slotBox = new ComboBox { ItemsSource = MealPlan.Slots, SelectedItem = "Dinner", Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(recipeBox, "Recipe");
        AutomationProperties.SetName(slotBox, "Meal");
        var ok = new Button { Content = "Add", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, Margin = new Thickness(0, 14, 0, 0) };
        ok.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(new TextBlock { Text = "Recipe", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(recipeBox);
        panel.Children.Add(new TextBlock { Text = "Meal", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(slotBox);
        panel.Children.Add(ok);
        dialog.Content = panel;
        if (dialog.ShowDialog() == true && recipeBox.SelectedItem is Recipe chosen)
        {
            _plan.Entries.Add(new MealPlanEntry { Date = day, Slot = slotBox.SelectedItem as string ?? "Dinner", RecipeId = chosen.Id });
            SavePlan();
            Render();
        }
    }

    private void SavePlan()
    {
        try
        {
            _plan.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "The meal plan could not be saved.\n\n" + exception.Message, "Meal planner");
        }
    }
}
