using System.Windows;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Equalizer.Application.DependencyInjection;
using Equalizer.Infrastructure.DependencyInjection;
using Equalizer.Presentation.Overlay;
using Equalizer.Presentation.Tray;
using Equalizer.Presentation.Hotkeys;
using Equalizer.Application.Abstractions;
using Equalizer.Presentation.Splash;

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

        var splash = new SplashWindow();
        splash.Show();

        // Fire-and-forget async startup so we don't block the UI thread
        _ = InitializeAsync(splash);
    }

    private async Task InitializeAsync(SplashWindow splash)
    {
        try
        {
            splash.SetStatus("Building services...");

            // Small pause so the user can see each startup phase
            await Task.Delay(180);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddEqualizerApplication();
                    services.AddEqualizerInfrastructure();
                    services.AddSingleton<IOverlayManager, MultiMonitorOverlayManager>();
                    services.AddTransient( typeof(Overlay.OverlayWindow));
                    services.AddTransient(typeof(Settings.SettingsWindow));
                    services.AddHostedService<TrayIconHostedService>();
                    services.AddHostedService<GlobalHotkeyService>();
                })
                .Build();

            splash.SetStatus("Starting background services...");
            await Task.Delay(180);
            await _host.StartAsync();

            splash.SetStatus("Loading settings and caching data...");
            await Task.Delay(180);
            var settingsPort = _host.Services.GetRequiredService<ISettingsPort>();
            var overlay = _host.Services.GetRequiredService<IOverlayManager>();
            var s = await settingsPort.GetAsync();

            if (s.OverlayVisible)
            {
                splash.SetStatus("Restoring overlay...");
                await Task.Delay(180);
                await overlay.ShowAsync();
            }

            splash.SetStatus("Ready");
            await Task.Delay(220);
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            splash.Dispatcher.Invoke(() => splash.Close());
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

