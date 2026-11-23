using GpsdExporter;
using GpsdExporter.Configuration;
using GpsdExporter.Metrics;

// Exit codes: 0 normal, 2 invalid command line or configuration.
const int InvalidConfiguration = 2;

var commandLine = CommandLine.Parse(args);
if (commandLine.ShowVersion)
{
    Console.Out.WriteLine($"gpsd-exporter {BuildInfo.Version} (revision {BuildInfo.Revision}, {BuildInfo.Runtime})");
    return 0;
}

if (commandLine.ShowHelp)
{
    Console.Out.WriteLine(CommandLine.Usage);
    return 0;
}

if (commandLine.Errors.Count > 0)
{
    return Fail(commandLine.Errors, showUsage: true);
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [.. commandLine.HostArgs] });
builder.Configuration.AddExporterSources(commandLine.ConfigArgs);

var (settings, errors) = ExporterSettings.Load(builder.Configuration);
if (settings is null)
{
    return Fail(errors, showUsage: false);
}

builder.ConfigureExporterHost(settings);
builder.Services.AddGpsdExporter(settings);

var app = builder.Build();
if (commandLine.PollIgnored)
{
    StartupLog.PollIgnored(app.Logger);
}

app.MapGpsdMetrics(settings.Http.MetricsPath);

await app.RunAsync().ConfigureAwait(false);
return 0;

static int Fail(IReadOnlyList<string> problems, bool showUsage)
{
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
