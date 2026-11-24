var builder = WebApplication.CreateSlimBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync().ConfigureAwait(false);

// Lets WebApplicationFactory<Program> find the entry point in the host tests.
public partial class Program;
