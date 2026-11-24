var builder = DistributedApplication.CreateBuilder(args);

builder.AddDotnetProject("gpsd-exporter", "../GpsdExporter/GpsdExporter.csproj");

builder.Build().Run();
