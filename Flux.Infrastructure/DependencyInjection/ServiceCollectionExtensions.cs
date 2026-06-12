using Microsoft.Extensions.DependencyInjection;
using Flux.Application.Abstractions;
using Flux.Infrastructure.Audio;
using Flux.Infrastructure.Settings;
using Flux.Infrastructure.Widgets;

namespace Flux.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEqualizerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAudioInputPort>(sp =>
        {
            var settings = sp.GetService<ISettingsPort>()?.GetAsync().GetAwaiter().GetResult();
            return new WASAPILoopbackAudioInput(settings?.AudioDeviceId);
        });
        services.AddSingleton<IAudioDeviceProvider, AudioDeviceProvider>();
        services.AddSingleton<ISettingsPort, JsonSettingsRepository>();
        services.AddSingleton<IWidgetLayoutPort, JsonWidgetLayoutRepository>();
        return services;
    }
}
