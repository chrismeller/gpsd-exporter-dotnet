using GpsdExporter.Connection;
using GpsdExporter.State;

namespace GpsdExporter;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// The gpsd side of the exporter: state, connection stats and the connection manager.
    /// Options use the spec defaults until configuration binding is added.
    /// </summary>
    public static IServiceCollection AddGpsdExporter(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new GpsdOptions());
        services.AddSingleton(new StalenessOptions());
        services.AddSingleton(sp => new StateStore(sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<StalenessOptions>()));
        services.AddSingleton<ConnectionStats>();
        services.AddHostedService<GpsdConnectionManager>();
        return services;
    }
}
