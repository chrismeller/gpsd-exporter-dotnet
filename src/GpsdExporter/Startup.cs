using GpsdExporter.Configuration;
using Microsoft.Extensions.Options;

namespace GpsdExporter;

/// <summary>
/// Starts the exporter and turns start-up failures into exit codes: 0 normal, 1 the HTTP port
/// could not be bound, 2 invalid command line or configuration. A gpsd connection problem
/// never ends the process.
/// </summary>
internal static partial class Startup
{
    public const int Success = 0;
    public const int BindFailed = 1;
    public const int InvalidConfiguration = 2;

    /// <summary>Validates the options, starts the host and runs it until shutdown.</summary>
    public static async Task<int> RunExporterAsync(this WebApplication app, bool pollIgnored)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The host would run this validation as it starts; running it first separates a
        // configuration problem from anything else that could fail on the way up.
        try
        {
            app.Services.GetService<IStartupValidator>()?.Validate();
        }
        catch (Exception e) when (ConfigurationProblems(e) is { } problems)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            return Fail(problems, showUsage: false);
        }

        if (pollIgnored)
        {
            PollIgnored(app.Logger);
        }

        try
        {
            await app.StartAsync().ConfigureAwait(false);
        }
        catch (IOException e)
        {
            // Kestrel could not bind the listen address (in use, or not permitted).
            await Console.Error.WriteLineAsync($"gpsd-exporter: {e.Message}").ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
            return BindFailed;
        }

        await app.WaitForShutdownAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        return Success;
    }

    /// <summary>Writes each problem to standard error and returns <see cref="InvalidConfiguration"/>.</summary>
    public static int Fail(IEnumerable<string> problems, bool showUsage)
    {
        ArgumentNullException.ThrowIfNull(problems);
        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"gpsd-exporter: {problem}");
        }

        if (showUsage)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLine.Usage);
        }

        return InvalidConfiguration;
    }

    /// <summary>
    /// The messages of a failed options validation, or of a value that could not be bound
    /// (the configuration binder throws <see cref="InvalidOperationException"/>); null for
    /// anything else.
    /// </summary>
    private static List<string>? ConfigurationProblems(Exception e) => e switch
    {
        OptionsValidationException validation => [.. validation.Failures],
        AggregateException aggregate when aggregate.InnerExceptions.All(static i => i is OptionsValidationException) =>
            [.. aggregate.InnerExceptions.Cast<OptionsValidationException>().SelectMany(static v => v.Failures)],
        InvalidOperationException binding => [binding.Message],
        _ => null,
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "-p is ignored: the exporter streams from gpsd with ?WATCH and does not poll.")]
    private static partial void PollIgnored(ILogger logger);
}
