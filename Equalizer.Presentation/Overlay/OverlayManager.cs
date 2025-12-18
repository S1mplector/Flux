using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Equalizer.Presentation.Interop;
using Forms = System.Windows.Forms;

namespace Equalizer.Presentation.Overlay;

public sealed class OverlayManager : IOverlayManager
{
    private readonly MainWindow _window;
    private bool _clickThrough;
    private bool _alwaysOnTop;

    public OverlayManager(MainWindow window)
    {
        _window = window;
        ConfigureOverlayWindow(_window);
        _window.SourceInitialized += (_, __) => WindowStyles.ApplyOverlayExtendedStyles(_window, _clickThrough);
    }

    public bool IsVisible => _window.Dispatcher.CheckAccess() ? _window.IsVisible : _window.Dispatcher.Invoke(() => _window.IsVisible);
    public bool ClickThrough => _clickThrough;
    public bool AlwaysOnTop => _alwaysOnTop;
    public double? GetCurrentFps() => null;

    public Task ShowAsync()
    {
        return _window.Dispatcher.InvokeAsync(() =>
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
        }).Task;
    }

    public Task HideAsync()
    {
        return _window.Dispatcher.InvokeAsync(() =>
        {
            if (_window.IsVisible)
            {
                _window.Hide();
            }
        }).Task;
    }

    public Task ToggleAsync()
    {
        return IsVisible ? HideAsync() : ShowAsync();
    }

    public Task SetClickThroughAsync(bool value)
    {
        _clickThrough = value;
        return _window.Dispatcher.InvokeAsync(() =>
        {
            WindowStyles.ApplyOverlayExtendedStyles(_window, _clickThrough);
        }).Task;
    }

    public Task ToggleClickThroughAsync() => SetClickThroughAsync(!_clickThrough);

    public Task SetAlwaysOnTopAsync(bool value)
    {
        _alwaysOnTop = value;
        return _window.Dispatcher.InvokeAsync(() =>
        {
            _window.Topmost = _alwaysOnTop;
        }).Task;
    }

    public Task ToggleAlwaysOnTopAsync() => SetAlwaysOnTopAsync(!_alwaysOnTop);

    private static void ConfigureOverlayWindow(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.AllowsTransparency = true;
        window.Background = System.Windows.Media.Brushes.Transparent;
        window.Topmost = false;
        window.WindowState = WindowState.Maximized;
    }

    public Task ResetPositionAsync() => Task.CompletedTask;
    
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        return Forms.Screen.AllScreens.Select(s => new MonitorInfo(
            s.DeviceName,
            $"Display - {s.Bounds.Width}x{s.Bounds.Height}",
            s.Bounds.Width,
            s.Bounds.Height,
            s.Primary,
            IsVisible
        )).ToList();
    }
    
    public Task RefreshMonitorsAsync() => Task.CompletedTask;
}
