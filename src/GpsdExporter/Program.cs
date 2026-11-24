using GpsdExporter;
using GpsdExporter.Configuration;

var commandLine = CommandLine.Parse(args);
if (commandLine.ShowVersion)
{
    Console.Out.WriteLine(BuildInfo.VersionLine);
    return Startup.Success;
}

if (commandLine.ShowHelp)
{
    Console.Out.WriteLine(CommandLine.Usage);
    return Startup.Success;
}

// appsettings.json is read once and not watched. Watching it puts an inotify watch on every
// directory under the content root, which is the working directory: under systemd that is /,
// and startup then takes from seconds to minutes before the HTTP port is open.
WebApplicationBuilder builder;
try
{
    builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
    {
        Args = [.. commandLine.ConfigArgs, "--hostBuilder:reloadConfigOnChange=false"],
    });
    builder.Configuration.AddExporterSources(commandLine.ConfigArgs);
}
catch (FormatException e)
{
    // A single-dash flag with a value that is not one of the exporter's.
    return Startup.Fail([e.Message], showUsage: true);
}

builder.ConfigureExporterHost();
builder.AddServiceDefaults();
builder.AddGpsdExporter();

var app = builder.Build();

app.MapDefaultEndpoints();

return await app.RunExporterAsync(commandLine.PollIgnored).ConfigureAwait(false);

// Lets WebApplicationFactory<Program> find the entry point in the host tests.
public partial class Program;
