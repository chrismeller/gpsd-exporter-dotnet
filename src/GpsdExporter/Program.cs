using GpsdExporter;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();
builder.AddGpsdExporter();
builder.AddGpsdHealthCheck();

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync().ConfigureAwait(false);

// Lets WebApplicationFactory<Program> find the entry point in the host tests.
public partial class Program;
