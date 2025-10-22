using Microsoft.Extensions.DependencyInjection;
using Equalizer.Application.Abstractions;
using Equalizer.Application.Services;

namespace Equalizer.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEqualizerApplication(this IServiceCollection services)
    {
        services.AddSingleton<IEqualizerService, EqualizerService>();
        services.AddSingleton<SpectrumProcessor>();
        return services;
    }
}
