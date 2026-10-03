using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;

namespace RecipeTool.App;

public partial class ExtensionSetupWindow : Window
{
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE5, 0x1C, 0x23));
    private readonly string _folder;
    private readonly Func<bool> _isConnected;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _edge;
    private string Browser => _edge ? "Edge" : "Chrome";
    private string Url => _edge ? "edge://extensions" : "chrome://extensions";
    private int _step;
    private const int LastStep = 4;

    public ExtensionSetupWindow(string folder, Func<bool> isConnected)
    {
        InitializeComponent();
        _folder = folder;
        _isConnected = isConnected;
        _edge = FindBrowser(false) is null && FindBrowser(true) is not null;
        _timer.Tick += (_, _) => UpdateConnection();
        Closed += (_, _) => _timer.Stop();
        ShowStep();
    }

    private void Browser_Click(object sender, RoutedEventArgs e)
    {
        _edge = !_edge;
        ShowStep();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
        {
            _step--;
            ShowStep();
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == LastStep)
        {
            Close();
            return;
        }

        _step++;
        ShowStep();
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_step)
            {
                case 0:
                    OpenExtensionsPage();
                    break;
                case 3:
                    Clipboard.SetText(_folder);
                    ActionButton.Content = "Copied ✓";
                    break;
                case 4:
                    Process.Start(new ProcessStartInfo("https://www.allrecipes.com/recipe/10813/best-chocolate-chip-cookies/")
                    { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(exception.Message, "Barb's Recipe Book", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? FindBrowser(bool edge)
    {
        var exeName = edge ? "msedge.exe" : "chrome.exe";
        foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            if (key?.GetValue(null) as string is { } registered && File.Exists(registered))
            {
                return registered;
            }
        }

        var relative = edge ? @"Microsoft\Edge\Application\msedge.exe" : @"Google\Chrome\Application\chrome.exe";
        return new[]
        {
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.LocalApplicationData
        }.Select(folder => Path.Combine(Environment.GetFolderPath(folder), relative)).FirstOrDefault(File.Exists);
    }

    private void OpenExtensionsPage()
    {
        // Browsers ignore chrome:// and edge:// addresses passed on the command line, so the address is copied for pasting.
        Clipboard.SetText(Url);
        var exe = FindBrowser(_edge);
        if (exe is null)
        {
            MessageBox.Show($"{Browser} wasn't found on this computer. Install it first, or use the other browser.",
                "Barb's Recipe Book", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        ActionButton.Content = "Opened ✓  (click again to re-copy the address)";
    }
    private void ShowStep()
    {
        _timer.Stop();
        StepCounter.Text = $"Step {_step + 1} of {LastStep + 1}";
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == LastStep ? "Finish" : "Next →";
        ActionButton.Visibility = Visibility.Visible;
        Title = $"Set up {Browser} capture";
        BrowserButton.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        BrowserButton.Content = _edge ? "I use Google Chrome instead" : "I use Microsoft Edge instead";
        Picture.Children.Clear();
        DrawBrowserFrame(_step == 3 ? "Open" : "Extensions", _step == 0);

        switch (_step)
        {
            case 0:
                StepTitle.Text = $"Open {Browser}'s extensions page";
                StepText.Text = $"Click the red button below. {Browser} opens and the address {Url} is copied for you. Click the address bar at the top of {Browser}, press Ctrl+V, then press Enter. You will land on a page called “Extensions”.";
                StepHint.Text = $"You can also just type {Url} into the address bar yourself. Your screen may look a little different from the picture, and that is fine.";
                ActionButton.Content = $"Open {Browser} and copy the address";
                break;
            case 1:
                StepTitle.Text = "Turn on “Developer mode”";
                StepText.Text = _edge
                    ? "Look at the menu on the left side of the page, near the bottom. Click the small switch next to “Developer mode” so it turns on."
                    : "Look at the top-right corner of the Chrome page. Click the small switch next to “Developer mode” so it turns blue.";
                StepHint.Text = "This only lets the browser add Barb's Recipe Book. It's safe, and you only do it once.";
                ActionButton.Visibility = Visibility.Collapsed;
                DrawExtensionsPage(developerOn: false, showCard: false);
                if (_edge)
                {
                    Arrow(260, 280, 150, 290);
                    Ring(8, 276, 150, 32);
                }
                else
                {
                    Arrow(560, 175, 628, 108);
                    Ring(620, 76, 58, 32);
                }
                break;
            case 2:
                StepTitle.Text = "Click “Load unpacked”";
                StepText.Text = "New buttons appear at the top left. Click the one called “Load unpacked”.";
                StepHint.Text = "A window will open asking you to choose a folder. That's the next step.";
                ActionButton.Visibility = Visibility.Collapsed;
                DrawExtensionsPage(developerOn: true, showCard: false);
                Arrow(170, 225, 92, 162);
                Ring(20, 122, 118, 38);
                break;
            case 3:
                StepTitle.Text = "Choose the extension folder";
                StepText.Text = "In the window that opens, click the “Folder” box at the bottom, press Ctrl+V to paste the folder, press Enter, then click “Select Folder”.";
                StepHint.Text = $"The folder is: {_folder}\nIf you can't paste, click the button below to copy it again.";
                ActionButton.Content = "Copy folder location";
                Clipboard.SetText(_folder);
                DrawFileDialog();
                break;
            default:
                StepTitle.Text = "All set — open a recipe page";
                StepText.Text = $"You should now see “Barb's Recipe Book Capture” in the list with its switch turned on. Open any recipe web page in {Browser}, then click the camera button.";
                StepHint.Text = "";
                ActionButton.Content = "Open a sample recipe page";
                DrawExtensionsPage(developerOn: true, showCard: true);
                Arrow(650, 250, 592, 225);
                Ring(21, 174, 568, 94);
                Picture.Children.Add(MakeText("Checking connection…", 14, true, Brushes.DimGray, 410, 290, "ConnectionLabel"));
                _timer.Start();
                UpdateConnection();
                break;
        }
    }

    private void UpdateConnection()
    {
        var label = Picture.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "ConnectionLabel");
        if (label is null)
        {
            return;
        }

        if (_isConnected())
        {
            label.Text = "✔ Connected! The camera button is ready.";
            label.Foreground = Brushes.ForestGreen;
            StepHint.Text = $"{Browser} is connected to Barb's Recipe Book. You're done!";
        }
        else
        {
            label.Text = $"Waiting for {Browser}… (open any web page)";
            label.Foreground = Brushes.DimGray;
        }
    }

    private void DrawBrowserFrame(string tabTitle, bool highlightAddress)
    {
        Rect(0, 0, 702, 338, Brushes.White, null, 0);
        Rect(0, 0, 702, 30, Brush("#DEE1E6"), null, 0);
        Rect(8, 5, 150, 25, Brushes.White, null, 8);
        Picture.Children.Add(MakeText(tabTitle, 12, false, Brushes.Black, 22, 9));
        Rect(0, 30, 702, 36, Brushes.White, null, 0);
        Rect(70, 36, 440, 24, Brush("#F1F3F4"), null, 12);
        Picture.Children.Add(MakeText(Url, 13, false, Brushes.Black, 90, 39));
        Picture.Children.Add(MakeText("←  →  ⟳", 15, false, Brushes.Gray, 10, 36));
        if (highlightAddress)
        {
            Ring(66, 33, 448, 30);
            Arrow(620, 140, 400, 66);
            Picture.Children.Add(MakeText($"{Browser}'s address bar", 13, true, Red, 520, 150));
            Picture.Children.Add(MakeText("Type the address here\nif the button doesn't work", 12, false, Brushes.DimGray, 520, 170));
        }
    }

    private void DrawExtensionsPage(bool developerOn, bool showCard)
    {
        Rect(0, 66, 702, 272, Brush("#F8F9FA"), null, 0);
        Rect(0, 66, 702, 52, Brush("#3C4043"), null, 0);
        Picture.Children.Add(MakeText("Extensions", 18, true, Brushes.White, 18, 78));
        if (_edge)
        {
            Rect(0, 118, 150, 220, Brush("#EEF0F3"), null, 0);
            Picture.Children.Add(MakeText("My extensions", 12, false, Brushes.Black, 14, 130));
            Picture.Children.Add(MakeText("Developer mode", 12, false, Brushes.Black, 14, 284));
            Rect(110, 282, 34, 18, developerOn ? Brush("#0F6CBD") : Brush("#9AA0A6"), null, 9);
            Ellipse(developerOn ? 128 : 112, 284, 14, Brushes.White);
        }
        else
        {
            Picture.Children.Add(MakeText("Developer mode", 13, false, Brushes.White, 520, 82));
            Rect(626, 80, 46, 24, developerOn ? Brush("#8AB4F8") : Brush("#9AA0A6"), null, 12);
            Ellipse(developerOn ? 650 : 630, 83, 18, Brushes.White);
        }

        if (developerOn)
        {
            Rect(0, 118, 702, 46, Brush("#E8EAED"), null, 0);
            DrawChromeButton(24, 126, 110, "Load unpacked");
            DrawChromeButton(146, 126, 120, "Pack extension");
            DrawChromeButton(278, 126, 70, "Update");
        }

        if (showCard)
        {
            Rect(25, 178, 560, 86, Brushes.White, Brush("#DADCE0"), 8);
            Picture.Children.Add(MakeText("Barb's Recipe Book Capture", 16, true, Brushes.Black, 45, 192));
            Picture.Children.Add(MakeText($"Send a recipe from the active {Browser} webpage…", 12, false, Brushes.DimGray, 45, 218));
            Rect(520, 200, 46, 24, Brush("#1A73E8"), null, 12);
            Ellipse(544, 203, 18, Brushes.White);
        }
    }

    private void DrawChromeButton(double x, double y, double width, string label)
    {
        Rect(x, y, width, 30, Brushes.White, Brush("#DADCE0"), 6);
        Picture.Children.Add(MakeText(label, 12, false, Brush("#1A73E8"), x + 10, y + 7));
    }

    private void DrawFileDialog()
    {
        Rect(0, 66, 702, 272, Brush("#6B6B6B"), null, 0);
        Rect(70, 78, 560, 252, Brushes.White, Brush("#888888"), 4);
        Picture.Children.Add(MakeText("Select the extension folder to load", 13, false, Brushes.Black, 84, 86));
        Rect(84, 112, 532, 130, Brush("#F6F6F6"), Brush("#CCCCCC"), 0);
        Picture.Children.Add(MakeText("📁 ChromeExtension", 13, false, Brushes.Black, 96, 122));
        Picture.Children.Add(MakeText("Folder:", 13, false, Brushes.Black, 84, 262));
        Rect(140, 258, 470, 26, Brushes.White, Brush("#7A7A7A"), 0);
        Picture.Children.Add(MakeText("Press Ctrl+V", 12, false, Brushes.DimGray, 148, 263));
        Rect(400, 294, 110, 28, Brush("#E1E1E1"), Brush("#ADADAD"), 3);
        Picture.Children.Add(MakeText("Select Folder", 12, false, Brushes.Black, 418, 300));
        Rect(520, 294, 90, 28, Brush("#E1E1E1"), Brush("#ADADAD"), 3);
        Picture.Children.Add(MakeText("Cancel", 12, false, Brushes.Black, 546, 300));
        Ring(138, 255, 476, 32);
        Picture.Children.Add(MakeText("① Paste here", 13, true, Red, 500, 263));
        Ring(396, 291, 118, 34);
        Picture.Children.Add(MakeText("② Then click", 13, true, Red, 270, 300));
        Arrow(375, 308, 394, 308);
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void Rect(double x, double y, double w, double h, Brush fill, Brush? stroke, double radius)
    {
        var rectangle = new Rectangle
        {
            Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = stroke is null ? 0 : 1,
            RadiusX = radius, RadiusY = radius
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        Picture.Children.Add(rectangle);
    }

    private void Ellipse(double x, double y, double size, Brush fill)
    {
        var ellipse = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = fill };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        Picture.Children.Add(ellipse);
    }

    private static TextBlock MakeText(string text, double size, bool bold, Brush color, double x, double y, string? name = null)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = size, Foreground = color,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Name = name ?? ""
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        return block;
    }

    private void Ring(double x, double y, double w, double h)
    {
        var ring = new Rectangle
        {
            Width = w, Height = h, Stroke = Red, StrokeThickness = 3, RadiusX = 8, RadiusY = 8
        };
        ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(650))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
        });
        Canvas.SetLeft(ring, x);
        Canvas.SetTop(ring, y);
        Picture.Children.Add(ring);
    }

    // Red arrow whose tip is at (toX, toY).
    private void Arrow(double fromX, double fromY, double toX, double toY)
    {
        var angle = Math.Atan2(toY - fromY, toX - fromX);
        const double head = 16;
        Point Wing(double offset) => new(toX - head * Math.Cos(angle + offset), toY - head * Math.Sin(angle + offset));
        var geometry = new PathGeometry();
        geometry.Figures.Add(new PathFigure(new Point(fromX, fromY), [new LineSegment(new Point(toX, toY), true)], false));
        geometry.Figures.Add(new PathFigure(Wing(0.5), [new LineSegment(new Point(toX, toY), true), new LineSegment(Wing(-0.5), true)], false));
        Picture.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry, Stroke = Red, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        });
    }
}

