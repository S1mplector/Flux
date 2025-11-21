using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Equalizer.Application.DependencyInjection;
using Equalizer.Infrastructure.DependencyInjection;
using Equalizer.Presentation.Overlay;
using Equalizer.Presentation.Tray;
using Equalizer.Presentation.Hotkeys;
using Equalizer.Application.Abstractions;

namespace Equalizer.Presentation;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    public static bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddEqualizerApplication();
                services.AddEqualizerInfrastructure();
                services.AddSingleton<IOverlayManager, MultiMonitorOverlayManager>();
                services.AddTransient<Overlay.OverlayWindow>();
                services.AddTransient<Settings.SettingsWindow>();
                services.AddHostedService<TrayIconHostedService>();
                services.AddHostedService<GlobalHotkeyService>();
            })
            .Build();

        // Start host so hosted services (tray icon) run
        _host.StartAsync().GetAwaiter().GetResult();

        // Restore overlay visibility from last session
        var settingsPort = _host.Services.GetRequiredService<ISettingsPort>();
        var overlay = _host.Services.GetRequiredService<IOverlayManager>();
        var s = settingsPort.GetAsync().GetAwaiter().GetResult();
        if (s.OverlayVisible)
        {
            overlay.ShowAsync().GetAwaiter().GetResult();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        if (_host != null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}

