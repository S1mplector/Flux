using Microsoft.Extensions.DependencyInjection;
using Equalizer.Application.Abstractions;
using Equalizer.Infrastructure.Audio;
using Equalizer.Infrastructure.Settings;

namespace Equalizer.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEqualizerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAudioInputPort, WASAPILoopbackAudioInput>();
        services.AddSingleton<ISettingsPort, InMemorySettingsRepository>();
        return services;
    }
}
