using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Flux.Avalonia.Views;

namespace Flux.Avalonia.Services;

public class TrayIconManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly OverlayManager _overlayManager;
    private TrayIcon? _trayIcon;
    private bool _disposed;

    public TrayIconManager(IServiceProvider services, OverlayManager overlayManager)
    {
        _services = services;
        _overlayManager = overlayManager;
    }

    public void Initialize()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            return;

        var menu = new NativeMenu();

        var toggleItem = new NativeMenuItem("Toggle Overlay");
        toggleItem.Click += async (_, _) => await _overlayManager.ToggleAsync();
        menu.Add(toggleItem);

        var resetItem = new NativeMenuItem("Reset Position");
        resetItem.Click += async (_, _) => await _overlayManager.ResetPositionAsync();
        menu.Add(resetItem);

        menu.Add(new NativeMenuItemSeparator());

        var clickThroughItem = new NativeMenuItem("Click-through");
        clickThroughItem.Click += async (_, _) =>
        {
            await _overlayManager.ToggleClickThroughAsync();
            clickThroughItem.Header = _overlayManager.ClickThrough ? "[x] Click-through" : "Click-through";
        };
        clickThroughItem.Header = _overlayManager.ClickThrough ? "[x] Click-through" : "Click-through";
        menu.Add(clickThroughItem);

        var alwaysOnTopItem = new NativeMenuItem("Always on Top");
        alwaysOnTopItem.Click += async (_, _) =>
        {
            await _overlayManager.ToggleAlwaysOnTopAsync();
            alwaysOnTopItem.Header = _overlayManager.AlwaysOnTop ? "[x] Always on Top" : "Always on Top";
        };
        menu.Add(alwaysOnTopItem);

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (_, _) =>
        {
            var settingsWindow = _services.GetRequiredService<SettingsWindow>();
            settingsWindow.Show();
            settingsWindow.Activate();
        };
        menu.Add(settingsItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };
        menu.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Flux",
            Menu = menu,
            IsVisible = true
        };

        // Icon will use default - custom icons can be added later
        _trayIcon.Clicked += async (_, _) => await _overlayManager.ToggleAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
