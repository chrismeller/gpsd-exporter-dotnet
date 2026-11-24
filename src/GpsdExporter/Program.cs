using GpsdExporter;
using GpsdExporter.Metrics;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();
builder.AddGpsdExporter();
builder.AddGpsdMetrics();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGpsdMetrics();

await app.RunAsync().ConfigureAwait(false);

// Lets WebApplicationFactory<Program> find the entry point in the host tests.
public partial class Program;
