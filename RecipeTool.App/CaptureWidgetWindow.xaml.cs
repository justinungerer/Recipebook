using System.Windows;
using System.Windows.Input;

namespace RecipeTool.App;

public partial class CaptureWidgetWindow : Window
{
    private readonly Action _capture;
    private Point _dragStart;
    private bool _dragging;

    public CaptureWidgetWindow(Action capture)
    {
        InitializeComponent();
        _capture = capture;
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 26;
        Top = area.Top + 160;
    }

    private readonly System.Windows.Threading.DispatcherTimer _resetTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public void ShowState(string glyph, bool autoReset)
    {
        _resetTimer.Stop();
        CameraButton.Content = glyph;
        if (!autoReset)
        {
            return;
        }

        _resetTimer.Tick -= ResetGlyph;
        _resetTimer.Tick += ResetGlyph;
        _resetTimer.Start();
    }

    private void ResetGlyph(object? sender, EventArgs e)
    {
        _resetTimer.Stop();
        CameraButton.Content = "📷";
    }

    private void CameraButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            return;
        }

        _capture();
    }

    private void CameraButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragging = false;
    }

    private void CameraButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            _dragging = false;
        }
    }
}
