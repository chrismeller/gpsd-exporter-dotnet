using GpsdExporter.Configuration;
using GpsdExporter.Connection;
using GpsdExporter.State;
using Microsoft.Extensions.Options;

namespace GpsdExporter;

internal static class GpsdExporterServices
{
    /// <summary>
    /// The gpsd side of the exporter: options, state, connection stats and the connection
    /// manager that keeps them up to date.
    /// </summary>
    public static TBuilder AddGpsdExporter<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<GpsdOptions>().BindConfiguration(GpsdOptions.Section);
        builder.Services.AddOptions<StalenessOptions>().BindConfiguration(StalenessOptions.Section);
        builder.Services.AddOptions<HttpOptions>().BindConfiguration(HttpOptions.Section);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<GpsdOptions>>().Value);
        builder.Services.AddSingleton(sp => new StateStore(
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<StalenessOptions>>().Value));
        builder.Services.AddSingleton<ConnectionStats>();
        builder.Services.AddHostedService<GpsdConnectionManager>();

        return builder;
    }
}
