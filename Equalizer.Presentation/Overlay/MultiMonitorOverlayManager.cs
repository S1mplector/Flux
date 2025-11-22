using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Equalizer.Presentation.Interop;
using Forms = System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Equalizer.Application.Abstractions;
using Equalizer.Domain;

namespace Equalizer.Presentation.Overlay;

public sealed class MultiMonitorOverlayManager : IOverlayManager
{
    private readonly IServiceProvider _services;
    private readonly ISettingsPort _settings;
    private readonly Dictionary<string, OverlayWindow> _windows = new();
    private bool _clickThrough;
    private bool _alwaysOnTop;

    public MultiMonitorOverlayManager(IServiceProvider services, ISettingsPort settings)
    {
        _services = services;
        _settings = settings;
    }

    public bool IsVisible => _windows.Values.Any(w => w.IsVisible);
    public bool ClickThrough => _clickThrough;
    public bool AlwaysOnTop => _alwaysOnTop;

    public async Task ShowAsync()
    {
        var s = await _settings.GetAsync();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var targets = GetTargetScreens(s);
            EnsureWindows(targets);
            foreach (var kv in _windows)
            {
                if (targets.Any(sc => sc.DeviceName == kv.Key))
                {
                    if (!kv.Value.IsVisible) kv.Value.Show();
                    ApplyStyles(kv.Value);
                }
                else
                {
                    if (kv.Value.IsVisible) kv.Value.Hide();
                }
            }
        });
        await SaveOverlayVisibleAsync(true);
    }

    public async Task HideAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var win in _windows.Values)
            {
                if (win.IsVisible) win.Hide();
            }
        });
        await SaveOverlayVisibleAsync(false);
    }

    public Task ToggleAsync() => IsVisible ? HideAsync() : ShowAsync();

    public Task SetClickThroughAsync(bool value)
    {
        _clickThrough = value;
        return System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var win in _windows.Values)
            {
                WindowStyles.ApplyOverlayExtendedStyles(win, _clickThrough);
            }
        }).Task;
    }

    public Task ToggleClickThroughAsync() => SetClickThroughAsync(!_clickThrough);

    public Task SetAlwaysOnTopAsync(bool value)
    {
        _alwaysOnTop = value;
        return System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var win in _windows.Values)
            {
                WindowStyles.SetTopMost(win, _alwaysOnTop);
                if (!_alwaysOnTop)
                {
                    WindowStyles.SendToBottom(win);
                }
            }
        }).Task;
    }

    public Task ToggleAlwaysOnTopAsync() => SetAlwaysOnTopAsync(!_alwaysOnTop);

    public async Task ResetPositionAsync()
    {
        var s = await _settings.GetAsync();
        var updated = new EqualizerSettings(
            s.BarsCount, s.Responsiveness, s.Smoothing, s.Color,
            s.TargetFps, s.ColorCycleEnabled, s.ColorCycleSpeedHz, s.BarCornerRadius,
            s.DisplayMode, s.SpecificMonitorDeviceName,
            offsetX: 0.0, offsetY: 0.0,
            s.VisualizerMode, s.CircleDiameter,
            s.OverlayVisible, s.FadeOnSilenceEnabled,
            s.SilenceFadeOutSeconds, s.SilenceFadeInSeconds,
            s.PitchReactiveColorEnabled);
        await _settings.SaveAsync(updated);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var win in _windows.Values)
            {
                win.ResetOffset();
            }
        });
    }

    private async Task SaveOverlayVisibleAsync(bool visible)
    {
        var s = await _settings.GetAsync();
        var updated = new EqualizerSettings(
            s.BarsCount, s.Responsiveness, s.Smoothing, s.Color,
            s.TargetFps, s.ColorCycleEnabled, s.ColorCycleSpeedHz, s.BarCornerRadius,
            s.DisplayMode, s.SpecificMonitorDeviceName,
            s.OffsetX, s.OffsetY,
            s.VisualizerMode, s.CircleDiameter,
            overlayVisible: visible,
            fadeOnSilenceEnabled: s.FadeOnSilenceEnabled,
            silenceFadeOutSeconds: s.SilenceFadeOutSeconds,
            silenceFadeInSeconds: s.SilenceFadeInSeconds,
            pitchReactiveColorEnabled: s.PitchReactiveColorEnabled);
        await _settings.SaveAsync(updated);
    }

    private void EnsureWindows(IEnumerable<Forms.Screen> targetScreens)
    {
        var screens = targetScreens;
        var keys = _windows.Keys.ToHashSet();
        var existing = new HashSet<string>();

        foreach (var screen in screens)
        {
            string key = screen.DeviceName;
            existing.Add(key);
            if (!_windows.ContainsKey(key))
            {
                var win = _services.GetRequiredService<OverlayWindow>();
                ConfigureForScreen(win, screen);
                _windows[key] = win;
            }
            else
            {
                ConfigureForScreen(_windows[key], screen);
            }
        }

        // Remove windows for screens no longer present
        foreach (var k in keys.Except(existing).ToList())
        {
            if (_windows.TryGetValue(k, out var w))
            {
                w.Close();
                _windows.Remove(k);
            }
        }
    }

    private static IEnumerable<Forms.Screen> GetTargetScreens(EqualizerSettings settings)
    {
        var all = Forms.Screen.AllScreens;
        switch (settings.DisplayMode)
        {
            case MonitorDisplayMode.PrimaryOnly:
                return new[] { Forms.Screen.PrimaryScreen ?? all.First() };
            case MonitorDisplayMode.Specific:
                var match = all.Where(s => string.Equals(s.DeviceName, settings.SpecificMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
                var selected = match.DefaultIfEmpty(Forms.Screen.PrimaryScreen ?? all.First());
                return selected;
            case MonitorDisplayMode.All:
            default:
                return all;
        }
    }

    private void ConfigureForScreen(OverlayWindow window, Forms.Screen screen)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var bounds = screen.Bounds; // pixels
        // Convert to WPF DPI-independent units (assume 96 DPI, WPF will scale per monitor automatically)
        double left = bounds.Left;
        double top = bounds.Top;
        double width = bounds.Width;
        double height = bounds.Height;
        window.Left = left;
        window.Top = top;
        window.Width = width;
        window.Height = height;
        WindowStyles.ApplyOverlayExtendedStyles(window, _clickThrough);
        WindowStyles.SetTopMost(window, _alwaysOnTop);
        if (!_alwaysOnTop) WindowStyles.SendToBottom(window);
    }

    private void ApplyStyles(OverlayWindow window)
    {
        WindowStyles.ApplyOverlayExtendedStyles(window, _clickThrough);
        WindowStyles.SetTopMost(window, _alwaysOnTop);
        if (!_alwaysOnTop) WindowStyles.SendToBottom(window);
    }
}
