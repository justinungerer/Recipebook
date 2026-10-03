using Microsoft.Win32;
using RecipeTool.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RecipeTool.App;

public partial class MainWindow : Window
{
    private readonly RecipeLibrary _library;
    private DispatcherTimer? _bridgeStatusTimer;
    private RecipeCaptureServer? _captureServer;
    private CaptureWidgetWindow? _captureWidget;
    private string _selectedCategory = "All recipes";
    private DateTimeOffset? _captureRequestedAt;
    private bool _isReady;
    private bool _loadingControls;
    private readonly MealPlan _plan = null!;
    private Recipe? _lastDeleted;
    private DispatcherTimer? _undoTimer;

    public MainWindow()
    {
        InitializeComponent();
        _bridgeStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _bridgeStatusTimer.Tick += (_, _) => UpdateCaptureStatus();
        var localDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BarbsRecipeBook");
        _library = new RecipeLibrary(Path.Combine(localDataDirectory, "recipes.json"));
        try
        {
            _library.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(
                $"Barb's recipe library could not be opened. The original file has not been changed.\n\n{_library.FilePath}\n\n{exception.Message}",
                "Could not open recipe library",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
            return;
        }

        if (RecipeService.PurgeTrash(_library.Recipes, DateTime.UtcNow) > 0)
        {
            _library.Save();
        }

        _plan = new MealPlan(Path.Combine(localDataDirectory, "mealplan.json"));
        try
        {
            _plan.Load();
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            _plan.Entries.Clear();
        }

        PopulateCategories();
        RefreshRecipes();
        _isReady = true;

        RefreshInstalledExtension();
        _captureServer = new RecipeCaptureServer(OnRecipeCapturedAsync);
        try
        {
            _captureServer.StartAsync().GetAwaiter().GetResult();
            _captureWidget = new CaptureWidgetWindow(RequestCapture);
            _captureWidget.Show();
            CaptureStatusText.Text = "Waiting for browser";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Net.Sockets.SocketException or IOException)
        {
            CaptureStatusText.Text = "Web capture unavailable";
            MessageBox.Show(
                $"The recipe book is ready, but the Chrome capture bridge could not start.\n\n{exception.Message}",
                "Web capture unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _bridgeStatusTimer.Start();
    }

    private void PopulateCategories()
    {
        var categories = new[] { "All recipes", RecipeService.FavoritesCategory }
            .Concat(RecipeService.StarterCategories)
            .Concat(_library.Recipes.Where(recipe => !recipe.IsDeleted).Select(recipe => recipe.Category))
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Append(RecipeService.TrashCategory)
            .ToList();
        CategoriesList.ItemsSource = categories;
        CategoriesList.SelectedItem = _selectedCategory;
    }

    private void RefreshRecipes(Guid? selectId = null)
    {
        var recipes = RecipeService.Search(_library.Recipes, SearchBox.Text, _selectedCategory);
        RecipesList.ItemsSource = recipes;
        var selection = selectId.HasValue
            ? recipes.FirstOrDefault(recipe => recipe.Id == selectId.Value)
            : recipes.FirstOrDefault(recipe => recipe.Id == (RecipesList.SelectedItem as Recipe)?.Id);
        RecipesList.SelectedItem = selection ?? recipes.FirstOrDefault();
        ShowRecipe(RecipesList.SelectedItem as Recipe);
    }

    private void ShowRecipe(Recipe? recipe)
    {
        var hasRecipe = recipe is not null;
        RecipeNameText.Text = hasRecipe ? recipe!.Name : "Welcome to Barb's recipe book";
        RecipeDescriptionText.Text = recipe?.Description ?? "Save family favorites, capture recipes from Chrome or Edge, and keep every version together.";
        RecipeCategoryText.Text = recipe?.Category ?? "";
        RecipeServingsText.Text = recipe is null ? "" : $"Serves {recipe.Servings}";
        RecipeVersionText.Text = recipe?.VersionName is { Length: > 0 } version ? $"Version: {version}" : "";
        RecipeTagsText.Text = recipe is null || recipe.Tags.Count == 0 ? "" : $"Tags: {string.Join(", ", recipe.Tags)}";
        var unitMode = UnitsBox.SelectedIndex;
        string Unit(string text, bool ingredient) => unitMode <= 0 ? text
            : IngredientMath.ConvertTemperatures(ingredient ? IngredientMath.Convert(text, unitMode == 1) : text, unitMode == 1);
        IngredientsText.Text = recipe is null ? "" : string.Join(Environment.NewLine, recipe.Ingredients.Select(line => Unit(line, true)));
        InstructionsText.Text = recipe is null
            ? ""
            : string.Join(Environment.NewLine + Environment.NewLine, recipe.Instructions.Select((step, index) => $"{index + 1}. {Unit(step, false)}"));
        var inTrash = _selectedCategory == RecipeService.TrashCategory;
        _loadingControls = true;
        RecipeActionsPanel.Visibility = hasRecipe && !inTrash ? Visibility.Visible : Visibility.Collapsed;
        FavoriteToggle.IsChecked = recipe?.IsFavorite == true;
        RatingBox.SelectedIndex = Math.Clamp(recipe?.Rating ?? 0, 0, 5);
        _loadingControls = false;
        LastMadeText.Text = recipe?.LastMade is { } made ? $"Last made {made.ToLocalTime():MMM d, yyyy}" : "";
        RecipeNotesText.Text = recipe is null ? "" : recipe.Notes;
        RecipeNotesText.Visibility = string.IsNullOrWhiteSpace(RecipeNotesText.Text) ? Visibility.Collapsed : Visibility.Visible;
        AdjustButton.Visibility = EditButton.Visibility = DeleteButton.Visibility = inTrash ? Visibility.Collapsed : Visibility.Visible;
        RestoreButton.Visibility = PurgeButton.Visibility = inTrash && hasRecipe ? Visibility.Visible : Visibility.Collapsed;
        var source = recipe?.SourceUrl?.Trim() ?? "";
        RecipeSourceLink.Tag = source;
        RecipeSourceText.ToolTip = source;
        RecipeSourceText.Visibility = Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
            && sourceUri.Scheme is "http" or "https" ? Visibility.Visible : Visibility.Collapsed;
        ReviewNotice.Visibility = recipe?.NeedsReview == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SourceLink_Click(object sender, RoutedEventArgs e)
    {
        var url = RecipeSourceLink.Tag as string ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show(this, "Could not open the link.", "Barb's Recipe Book");
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isReady)
        {
            RefreshRecipes();
        }
    }

    private void CategoriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoriesList.SelectedItem is string category)
        {
            _selectedCategory = category;
            if (_isReady)
            {
                RefreshRecipes();
            }
        }
    }

    private void RecipesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowRecipe(RecipesList.SelectedItem as Recipe);
    }

    private void AddRecipe_Click(object sender, RoutedEventArgs e)
    {
        var draft = new Recipe { Category = "Other" };
        EditAndSave(draft, isNew: true);
    }

    private void EditRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (RecipesList.SelectedItem is Recipe recipe)
        {
            EditAndSave(CopyRecipe(recipe), isNew: false);
        }
    }

    private void EditAndSave(Recipe draft, bool isNew)
    {
        var editor = new RecipeEditorWindow(draft, RecipeService.StarterCategories.Concat(_library.Recipes.Select(recipe => recipe.Category)))
        {
            Owner = this
        };
        if (editor.ShowDialog() != true || editor.ResultRecipe is not { } edited)
        {
            return;
        }

        var existing = _library.Recipes.FindIndex(recipe => recipe.Id == edited.Id);
        if (existing >= 0)
        {
            edited.UpdatedAt = DateTime.UtcNow;
            _library.Recipes[existing] = edited;
        }
        else if (isNew || edited.ParentRecipeId.HasValue)
        {
            edited.CreatedAt = DateTime.UtcNow;
            edited.UpdatedAt = edited.CreatedAt;
            _library.Recipes.Add(edited);
        }

        SaveAndRefresh(edited.Id);
    }

    private void AdjustedVersion_Click(object sender, RoutedEventArgs e)
    {
        if (RecipesList.SelectedItem is not Recipe original)
        {
            return;
        }

        var dialog = new AdjustedVersionWindow(original) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var adjusted = RecipeService.CreateAdjustedCopy(original, dialog.Servings, dialog.VersionName);
        EditAndSave(adjusted, isNew: true);
    }

    private void DeleteRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (RecipesList.SelectedItem is not Recipe recipe)
        {
            return;
        }

        recipe.DeletedAt = DateTime.UtcNow;
        _lastDeleted = recipe;
        SaveAndRefresh();
        UndoButton.Content = $"↶ Undo delete “{Shorten(recipe.Name)}”";
        UndoButton.Visibility = Visibility.Visible;
        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _undoTimer.Tick += (_, _) => HideUndo();
        _undoTimer.Start();
    }

    private static string Shorten(string text) => text.Length <= 24 ? text : text[..23] + "…";

    private void HideUndo()
    {
        _undoTimer?.Stop();
        UndoButton.Visibility = Visibility.Collapsed;
        _lastDeleted = null;
    }

    private void UndoDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDeleted is { } recipe)
        {
            recipe.DeletedAt = null;
            HideUndo();
            SaveAndRefresh(recipe.Id);
        }
    }

    private void RestoreRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (RecipesList.SelectedItem is Recipe recipe)
        {
            recipe.DeletedAt = null;
            SaveAndRefresh();
        }
    }

    private void PurgeRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (RecipesList.SelectedItem is Recipe recipe
            && MessageBox.Show($"Permanently delete “{recipe.Name}”? This cannot be undone.", "Delete forever",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _library.Recipes.RemoveAll(item => item.Id == recipe.Id);
            SaveAndRefresh();
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (RecipesList.SelectedItem is Recipe recipe)
        {
            recipe.IsFavorite = FavoriteToggle.IsChecked == true;
            SaveAndRefresh(recipe.Id);
        }
    }

    private void Rating_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingControls && _isReady && RecipesList.SelectedItem is Recipe recipe && recipe.Rating != RatingBox.SelectedIndex)
        {
            recipe.Rating = Math.Max(0, RatingBox.SelectedIndex);
            SaveAndRefresh(recipe.Id);
        }
    }

    private void MadeToday_Click(object sender, RoutedEventArgs e)
    {
        if (RecipesList.SelectedItem is Recipe recipe)
        {
            recipe.LastMade = DateTime.UtcNow;
            SaveAndRefresh(recipe.Id);
        }
    }

    private void Units_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isReady)
        {
            ShowRecipe(RecipesList.SelectedItem as Recipe);
        }
    }

    private void GroceryList_Click(object sender, RoutedEventArgs e)
        => new GroceryListWindow(_library.Recipes.Where(r => !r.IsDeleted).OrderBy(r => r.Name).ToList(), []) { Owner = this }.ShowDialog();

    private void MealPlanner_Click(object sender, RoutedEventArgs e)
        => new MealPlannerWindow(_plan, _library, SelectRecipe) { Owner = this }.Show();

    private void Pantry_Click(object sender, RoutedEventArgs e)
        => new PantryWindow(_library, SelectRecipe) { Owner = this }.Show();

    private void SelectRecipe(Guid id)
    {
        _selectedCategory = "All recipes";
        SearchBox.Text = "";
        PopulateCategories();
        RefreshRecipes(id);
        Activate();
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export recipe book backup",
            Filter = "Recipe book backup (*.json)|*.json",
            FileName = "Barbs-Recipe-Book.json",
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            RecipeLibrary.WriteBackup(dialog.FileName, _library.Recipes);
            MessageBox.Show($"Backup saved to:\n{dialog.FileName}", "Backup complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError("The backup could not be saved.", exception);
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a recipe book backup",
            Filter = "Recipe book backup (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (MessageBox.Show("Restoring this backup will replace the recipes currently in this library. Continue?",
                "Restore recipe book", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var restored = RecipeLibrary.ReadBackup(dialog.FileName);
            var safetyBackup = Path.Combine(
                Path.GetDirectoryName(_library.FilePath)!,
                $"recipes-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            RecipeLibrary.WriteBackup(safetyBackup, _library.Recipes);
            _library.Recipes.Clear();
            _library.Recipes.AddRange(restored);
            SaveAndRefresh();
            MessageBox.Show($"The previous library was also saved to:\n{safetyBackup}", "Restore complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            ShowError("The backup could not be restored. The library file was not replaced.", exception);
        }
    }

    // A visible folder in Documents is easy to find in the browser's folder picker.
    private static string ExtensionFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Barbs Recipe Book Extension");

    private static string WriteExtensionFiles()
    {
        var folder = ExtensionFolder;
        Directory.CreateDirectory(folder);
        var assembly = typeof(MainWindow).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("ChromeExtension/", StringComparison.Ordinal)))
        {
            using var source = assembly.GetManifestResourceStream(name)!;
            using var target = File.Create(Path.Combine(folder, name["ChromeExtension/".Length..]));
            source.CopyTo(target);
        }

        return folder;
    }

    // Keeps a previously installed extension folder current after the app is upgraded.
    private static void RefreshInstalledExtension()
    {
        try
        {
            if (Directory.Exists(ExtensionFolder))
            {
                WriteExtensionFiles();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Chrome may hold a file briefly; the next launch will retry.
        }
    }

    private void InstallExtension_Click(object sender, RoutedEventArgs e) => InstallExtension();

    private void InstallExtension()
    {
        try
        {
            var folder = WriteExtensionFiles();

            var wizard = new ExtensionSetupWindow(folder,
                () => _captureServer?.LastExtensionSeen is { } seen && DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(7))
            {
                Owner = this
            };
            wizard.Show();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            ShowError("The browser extension could not be prepared.", exception);
        }
    }

    private void OpenLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = Path.GetDirectoryName(_library.FilePath)
                ?? throw new InvalidOperationException("The local recipe folder path is invalid.");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowError("The local recipe folder could not be opened.", exception);
        }
    }

    private async Task OnRecipeCapturedAsync(CapturedRecipe captured)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            var recipe = captured.ToRecipe();
            if (string.IsNullOrWhiteSpace(recipe.Name))
            {
                recipe.Name = "Recipe from webpage";
            }

            recipe.Category = RecipeService.SuggestCategory(recipe.Name, recipe.Category);
            _captureRequestedAt = null;
            _captureWidget?.ShowState("✅", true);

            // Dialogs are shown after the request returns so the browser isn't kept waiting.
            if (RecipeService.FindDuplicate(_library.Recipes, recipe) is { } duplicate)
            {
                CaptureStatusText.Text = "Already in your recipe book";
                Dispatcher.BeginInvoke(() =>
                {
                    var answer = MessageBox.Show(
                        $"“{duplicate.Name}” is already in your recipe book.\n\nSave another copy anyway?",
                        "Already saved", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Yes)
                    {
                        AddCaptured(recipe);
                    }
                    else
                    {
                        SelectRecipe(duplicate.Id);
                    }
                });
                return;
            }

            AddCaptured(recipe);
        });
    }

    private void AddCaptured(Recipe recipe)
    {
        _library.Recipes.Add(recipe);
        CaptureStatusText.Text = "Recipe captured — review it";
        _selectedCategory = "All recipes";
        PopulateCategories();
        SaveAndRefresh(recipe.Id);
        Dispatcher.BeginInvoke(() => ChooseCategory(recipe));
    }

    private void ChooseCategory(Recipe recipe)
    {
        var picker = new CategoryPickerWindow(
            recipe.Name,
            RecipeService.StarterCategories.Concat(_library.Recipes.Select(item => item.Category)),
            recipe.Category);
        if (IsVisible)
        {
            picker.Owner = this;
            picker.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        if (picker.ShowDialog() == true
            && !string.Equals(picker.SelectedCategory, recipe.Category, StringComparison.Ordinal))
        {
            recipe.Category = picker.SelectedCategory;
            recipe.UpdatedAt = DateTime.UtcNow;
            _selectedCategory = "All recipes";
            PopulateCategories();
            SaveAndRefresh(recipe.Id);
        }

        Activate();
    }

    private void RequestCapture()
    {
        if (_captureServer is null)
        {
            MessageBox.Show("The Chrome capture bridge is not available.", "Capture unavailable",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var connected = _captureServer.LastExtensionSeen is { } seen
            && DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(7);
        if (connected && _captureServer.ExtensionVersion != RecipeCaptureServer.RequiredExtensionVersion)
        {
            _captureWidget?.ShowState("⚠️", true);
            CaptureStatusText.Text = "Browser extension is out of date";
            MessageBox.Show(
                "Chrome is still running an older copy of the capture extension, which can miss recipes.\n\n" +
                "Open chrome://extensions, find Barb's Recipe Book Capture, click its reload (circular arrow) button, " +
                "then reload the recipe page and try again.\n\n(The app has already updated the extension files.)",
                "Barb's Recipe Book", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _captureServer.RequestCapture();
        _captureRequestedAt = DateTimeOffset.UtcNow;
        CaptureStatusText.Text = "Waiting for active browser page…";
        _captureWidget?.ShowState("⏳", false);

        if (!connected)
        {
            _captureWidget?.ShowState("⚠️", true);
            CaptureStatusText.Text = "Browser extension not detected";
            var answer = MessageBox.Show(
                "The browser extension isn't connected yet, so the page can't be captured.\n\n" +
                "Set it up now? (one time). If it's already installed, choose No, reload the recipe page and try again.",
                "Barb's Recipe Book", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                InstallExtension();
            }
        }
    }

    private void UpdateCaptureStatus()
    {
        if (_captureServer is null)
        {
            return;
        }

        var connected = _captureServer.LastExtensionSeen is { } seen
            && DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(7);
        if (connected)
        {
            CaptureStatusText.Text = "Browser capture ready";
        }
        else if (_captureRequestedAt is { } requestedAt
            && DateTimeOffset.UtcNow - requestedAt >= TimeSpan.FromSeconds(30))
        {
            _captureRequestedAt = null;
            _captureWidget?.ShowState("⚠️", true);
            CaptureStatusText.Text = "No browser page responded; click the camera to retry";
        }
        else if (CaptureStatusText.Text != "Waiting for active browser page…")
        {
            CaptureStatusText.Text = "Open Chrome or Edge to capture";
        }
    }

    private void SaveAndRefresh(Guid? selectId = null)
    {
        try
        {
            _library.Save();
            PopulateCategories();
            RefreshRecipes(selectId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError($"The recipe is in memory but could not be saved to {_library.FilePath}. Check folder permissions and try again.", exception);
        }
    }

    private static Recipe CopyRecipe(Recipe recipe) => recipe.Clone();

    private void ShowError(string message, Exception exception)
    {
        MessageBox.Show($"{message}\n\n{exception.Message}", "Recipe book error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _bridgeStatusTimer?.Stop();
        _captureWidget?.Close();
        if (_captureServer is not null)
        {
            await _captureServer.StopAsync();
        }
    }
}


