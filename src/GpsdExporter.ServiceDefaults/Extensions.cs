using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// The Aspire service defaults: health checks and OpenTelemetry. Trimmed from the
// aspire-servicedefaults template: the exporter makes no outbound HTTP calls, so HttpClient
// resilience, service discovery and HttpClient instrumentation are left out.
// See https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string ReadinessEndpointPath = "/readyz";
    private const string AlivenessEndpointPath = "/healthz";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        return builder;
    }

    /// <summary>
    /// Logs, metrics and traces through OpenTelemetry, exported over OTLP when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set. The application adds its own meters and
    /// activity sources with <c>ConfigureOpenTelemetryMeterProvider</c> and
    /// <c>ConfigureOpenTelemetryTracerProvider</c>; this adds ASP.NET Core, .NET runtime and
    /// process metrics, and ASP.NET Core request spans.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(builder.Environment.ApplicationName, serviceVersion: ApplicationVersion())
                // A resource added later wins, so OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES
                // still override the name and version set above.
                .AddEnvironmentVariableDetector())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(aspNetCore =>
                    // Exclude health check requests from tracing
                    aspNetCore.Filter = context =>
                        !context.Request.Path.StartsWithSegments(ReadinessEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)));

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>The entry assembly's informational version, without build metadata (<c>+commit</c>).</summary>
    private static string? ApplicationVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = version?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        return plus < 0 ? version : version![..plus];
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // The OTLP exporter reads the other OTEL_EXPORTER_OTLP_* settings (protocol, headers,
        // timeout) from IConfiguration itself, so environment variables are all it needs.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live", "ready"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Mapped in every environment: the exporter runs unattended in containers and under
        // systemd, and the checks say only whether it is up, nothing sensitive.

        // Only health checks tagged with the "ready" tag must pass for app to be considered ready to accept traffic
        app.MapHealthChecks(ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready")
        });

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
