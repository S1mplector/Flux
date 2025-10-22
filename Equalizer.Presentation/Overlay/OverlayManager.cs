using System.Threading.Tasks;
using System.Windows;

namespace Equalizer.Presentation.Overlay;

public sealed class OverlayManager : IOverlayManager
{
    private readonly MainWindow _window;

    public OverlayManager(MainWindow window)
    {
        _window = window;
        ConfigureOverlayWindow(_window);
    }

    public bool IsVisible => _window.IsVisible;

    public Task ShowAsync()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
            _window.Activate();
        }
        else
        {
            _window.Topmost = true; // keep on top
            _window.Topmost = false;
            _window.Topmost = true;
        }
        return Task.CompletedTask;
    }

    public Task HideAsync()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        return Task.CompletedTask;
    }

    public Task ToggleAsync()
    {
        return _window.IsVisible ? HideAsync() : ShowAsync();
    }

    private static void ConfigureOverlayWindow(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.AllowsTransparency = true;
        window.Background = System.Windows.Media.Brushes.Transparent;
        window.Topmost = true;
        window.WindowState = WindowState.Maximized;
    }
}
