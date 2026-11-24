using GpsdExporter;
using GpsdExporter.Configuration;
using GpsdExporter.Http;

// Exit codes: 0 normal, 1 the HTTP port could not be bound, 2 invalid command line or
// configuration. A gpsd connection problem never ends the process (AC1.6).
const int BindFailed = 1;
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

// appsettings.json is read once and not watched. Watching it puts an inotify watch on every
// directory under the content root, which is the working directory: under systemd that is /,
// and startup then takes from seconds to minutes before the HTTP port is open.
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = [.. commandLine.HostArgs, "--hostBuilder:reloadConfigOnChange=false"],
});
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

app.MapExporterEndpoints(settings.Http.MetricsPath);

try
{
    await app.StartAsync().ConfigureAwait(false);
}
catch (IOException e)
{
    // Kestrel could not bind the listen address (in use, or not permitted).
    Console.Error.WriteLine($"gpsd-exporter: {e.Message}");
    await app.DisposeAsync().ConfigureAwait(false);
    return BindFailed;
}

await app.WaitForShutdownAsync().ConfigureAwait(false);
await app.DisposeAsync().ConfigureAwait(false);
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
