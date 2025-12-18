using Microsoft.Extensions.DependencyInjection;
using Equalizer.Application.Abstractions;
using Equalizer.Infrastructure.Audio;
using Equalizer.Infrastructure.Settings;
using Equalizer.Infrastructure.Widgets;

namespace Equalizer.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEqualizerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAudioInputPort, WASAPILoopbackAudioInput>();
        services.AddSingleton<ISettingsPort, JsonSettingsRepository>();
        services.AddSingleton<IAudioDeviceProvider, AudioDeviceProvider>();
        services.AddSingleton<IWidgetLayoutPort, JsonWidgetLayoutRepository>();
        return services;
    }
}
