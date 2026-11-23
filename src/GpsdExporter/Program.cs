using GpsdExporter;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddGpsdExporter();

var app = builder.Build();

app.Run();
