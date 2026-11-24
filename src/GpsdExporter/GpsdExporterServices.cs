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
    /// <remarks>
    /// Every options type is validated before the host starts (<c>ValidateOnStart</c>), and
    /// unknown keys in its section are errors, so a typo is reported instead of silently
    /// ignored. The <c>BindConfiguration</c> calls name their types so that the configuration
    /// binding source generator can replace them, which keeps them AOT-safe.
    /// </remarks>
    public static TBuilder AddGpsdExporter<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<GpsdOptions>().BindConfiguration(GpsdOptions.Section, Strict).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<GpsdOptions>, GpsdOptionsValidator>();
        builder.Services.AddOptions<StalenessOptions>().BindConfiguration(StalenessOptions.Section, Strict).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<StalenessOptions>, StalenessOptionsValidator>();
        builder.Services.AddOptions<HttpOptions>().BindConfiguration(HttpOptions.Section, Strict).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<HttpOptions>, HttpOptionsValidator>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<StateStore>();
        builder.Services.AddSingleton<ConnectionStats>();
        builder.Services.AddHostedService<GpsdConnectionManager>();

        return builder;
    }

    private static void Strict(BinderOptions binder) => binder.ErrorOnUnknownConfiguration = true;
}
