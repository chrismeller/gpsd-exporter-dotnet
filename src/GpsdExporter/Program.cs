using GpsdExporter;
using GpsdExporter.Metrics;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddGpsdExporter();

var app = builder.Build();
app.MapGpsdMetrics();

app.Run();
