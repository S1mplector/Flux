using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Flux.Application.Abstractions;
using Flux.Application.DependencyInjection;
using Flux.Infrastructure.DependencyInjection;
using Flux.Avalonia.Services;
using Flux.Avalonia.Views;

namespace Flux.Avalonia;

public partial class App : global::Avalonia.Application
{
    private IHost? _host;
    private CancellationTokenSource? _cts;

    public static bool IsShuttingDown { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        _cts = new CancellationTokenSource();

        // Build host with DI
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddEqualizerApplication();
                services.AddEqualizerInfrastructure();
                services.AddSingleton<IScreenProvider, AvaloniaScreenProvider>();
                
                // Views - use transient for windows that can be opened multiple times
                services.AddSingleton<MainWindow>();
                services.AddTransient<OverlayWindow>(sp => new OverlayWindow(sp));
                services.AddTransient<SettingsWindow>(sp => 
                    new SettingsWindow(
                        sp.GetRequiredService<ISettingsPort>(),
                        sp.GetRequiredService<IAudioDeviceProvider>()));
                
                // Managers
                services.AddSingleton<OverlayManager>();
                services.AddSingleton<TrayIconManager>();
            })
            .Build();

        await _host.StartAsync(_cts.Token);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Don't show main window - we're a tray app
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Initialize overlay and tray
            var overlayManager = _host.Services.GetRequiredService<OverlayManager>();
            var trayManager = _host.Services.GetRequiredService<TrayIconManager>();

            await overlayManager.InitializeAsync();
            trayManager.Initialize();

            // Start the visualizer
            var fluxService = _host.Services.GetRequiredService<IFluxService>();
            _ = fluxService.StartAsync(_cts.Token);

            desktop.Exit += async (_, _) =>
            {
                IsShuttingDown = true;
                _cts?.Cancel();
                trayManager.Dispose();
                await overlayManager.DisposeAsync();
                if (_host != null)
                {
                    await _host.StopAsync();
                    _host.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
