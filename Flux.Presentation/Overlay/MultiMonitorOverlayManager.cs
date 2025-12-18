using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Flux.Presentation.Interop;
using Forms = System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Flux.Application.Abstractions;
using Flux.Domain;

namespace Flux.Presentation.Overlay;

public sealed class MultiMonitorOverlayManager : IOverlayManager, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ISettingsPort _settings;
    private readonly Dictionary<string, OverlayWindow> _windows = new();
    private readonly DispatcherTimer _monitorCheckTimer;
    private bool _clickThrough;
    private bool _alwaysOnTop;
    private bool _isVisible;
    private int _lastMonitorCount;
    private bool _disposed;

    public MultiMonitorOverlayManager(IServiceProvider services, ISettingsPort settings)
    {
        _services = services;
        _settings = settings;
        _lastMonitorCount = Forms.Screen.AllScreens.Length;
        
        // Listen for display settings changes
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        
        // Periodic check for monitor changes (backup for edge cases)
        _monitorCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _monitorCheckTimer.Tick += OnMonitorCheckTick;
        _monitorCheckTimer.Start();
    }
    
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Display settings changed - refresh overlays
        if (_isVisible)
        {
            _ = RefreshMonitorsAsync();
        }
    }
    
    private void OnMonitorCheckTick(object? sender, EventArgs e)
    {
        var currentCount = Forms.Screen.AllScreens.Length;
        if (currentCount != _lastMonitorCount)
        {
            _lastMonitorCount = currentCount;
            if (_isVisible)
            {
                _ = RefreshMonitorsAsync();
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _monitorCheckTimer.Stop();
        
        foreach (var win in _windows.Values)
        {
            try { win.Close(); } catch { }
        }
        _windows.Clear();
    }

    public bool IsVisible => _isVisible;
    public bool ClickThrough => _clickThrough;
    public bool AlwaysOnTop => _alwaysOnTop;
    public double? GetCurrentFps()
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            return SampleFps();
        }

        return System.Windows.Application.Current.Dispatcher.Invoke<double?>(SampleFps);
    }

    private double? SampleFps()
    {
        var now = DateTime.UtcNow;
        var samples = _windows.Values
            .Where(w => w.IsVisible && now - w.LastFpsSampleAt <= TimeSpan.FromSeconds(2) && w.LastMeasuredFps > 0)
            .Select(w => w.LastMeasuredFps)
            .ToList();
        if (samples.Count == 0) return null;
        return samples.Average();
    }

    public async Task ShowAsync()
    {
        var s = await _settings.GetAsync();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var targets = GetTargetScreens(s);
            EnsureWindows(targets, s);
            foreach (var kv in _windows)
            {
                if (targets.Any(sc => sc.DeviceName == kv.Key))
                {
                    kv.Value.SyncOffset(s);
                    if (!kv.Value.IsVisible) kv.Value.Show();
                    ApplyStyles(kv.Value);
                }
                else
                {
                    if (kv.Value.IsVisible) kv.Value.Hide();
                }
            }
        });
        _isVisible = true;
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
        _isVisible = false;
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
        var updated = new FluxSettings(
            s.BarsCount, s.Responsiveness, s.Smoothing, s.Color,
            s.TargetFps, s.ColorCycleEnabled, s.ColorCycleSpeedHz, s.BarCornerRadius,
            s.DisplayMode, s.SpecificMonitorDeviceName,
            offsetX: 0.0, offsetY: 0.0,
            s.VisualizerMode, s.CircleDiameter,
            s.OverlayVisible, s.FadeOnSilenceEnabled,
            s.SilenceFadeOutSeconds, s.SilenceFadeInSeconds,
            pitchReactiveColorEnabled: s.PitchReactiveColorEnabled,
            s.BassEmphasis, s.TrebleEmphasis,
            beatShapeEnabled: s.BeatShapeEnabled, s.GlowEnabled, s.PerfOverlayEnabled,
            gradientEnabled: s.GradientEnabled, gradientEndColor: s.GradientEndColor,
            audioDeviceId: s.AudioDeviceId, renderingMode: s.RenderingMode,
            monitorOffsets: new Dictionary<string, MonitorOffset>(StringComparer.OrdinalIgnoreCase));
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
        var updated = new FluxSettings(
            s.BarsCount, s.Responsiveness, s.Smoothing, s.Color,
            s.TargetFps, s.ColorCycleEnabled, s.ColorCycleSpeedHz, s.BarCornerRadius,
            s.DisplayMode, s.SpecificMonitorDeviceName,
            s.OffsetX, s.OffsetY,
            s.VisualizerMode, s.CircleDiameter,
            overlayVisible: visible,
            fadeOnSilenceEnabled: s.FadeOnSilenceEnabled,
            silenceFadeOutSeconds: s.SilenceFadeOutSeconds,
            silenceFadeInSeconds: s.SilenceFadeInSeconds,
            pitchReactiveColorEnabled: s.PitchReactiveColorEnabled,
            bassEmphasis: s.BassEmphasis,
            trebleEmphasis: s.TrebleEmphasis,
            beatShapeEnabled: s.BeatShapeEnabled,
            glowEnabled: s.GlowEnabled,
            perfOverlayEnabled: s.PerfOverlayEnabled,
            gradientEnabled: s.GradientEnabled,
            gradientEndColor: s.GradientEndColor,
            audioDeviceId: s.AudioDeviceId,
            renderingMode: s.RenderingMode,
            monitorOffsets: s.MonitorOffsets);
        await _settings.SaveAsync(updated);
    }

    private void EnsureWindows(IEnumerable<Forms.Screen> targetScreens, FluxSettings settings)
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
                win.SetMonitorDevice(screen.DeviceName);
                win.SyncOffset(settings);
                _windows[key] = win;
            }
            else
            {
                ConfigureForScreen(_windows[key], screen);
                _windows[key].SetMonitorDevice(screen.DeviceName);
                _windows[key].SyncOffset(settings);
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

    private static IEnumerable<Forms.Screen> GetTargetScreens(FluxSettings settings)
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
    
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        var allScreens = Forms.Screen.AllScreens;
        
        foreach (var screen in allScreens)
        {
            var friendlyName = GetFriendlyMonitorName(screen);
            var hasOverlay = _windows.ContainsKey(screen.DeviceName) && 
                             _windows[screen.DeviceName].IsVisible;
            
            result.Add(new MonitorInfo(
                screen.DeviceName,
                friendlyName,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.Primary,
                hasOverlay));
        }
        
        return result;
    }
    
    private static string GetFriendlyMonitorName(Forms.Screen screen)
    {
        var name = screen.DeviceName;
        // Extract display number from device name like \\.\DISPLAY1
        if (name.StartsWith("\\\\.\\DISPLAY", StringComparison.OrdinalIgnoreCase))
        {
            var num = name.Substring(11);
            var suffix = screen.Primary ? " (Primary)" : "";
            return $"Display {num}{suffix} - {screen.Bounds.Width}x{screen.Bounds.Height}";
        }
        return $"{name} - {screen.Bounds.Width}x{screen.Bounds.Height}";
    }
    
    public async Task RefreshMonitorsAsync()
    {
        _lastMonitorCount = Forms.Screen.AllScreens.Length;
        
        if (_isVisible)
        {
            // Re-show to reconfigure for current monitors
            await ShowAsync();
        }
        else
        {
            // Just clean up stale windows
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var currentScreenNames = Forms.Screen.AllScreens.Select(s => s.DeviceName).ToHashSet();
                var stale = _windows.Keys.Where(k => !currentScreenNames.Contains(k)).ToList();
                foreach (var key in stale)
                {
                    if (_windows.TryGetValue(key, out var win))
                    {
                        win.Close();
                        _windows.Remove(key);
                    }
                }
            });
        }
    }
}
