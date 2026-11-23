# gpsd-exporter-dotnet

A Prometheus exporter for [gpsd](https://gpsd.io/), written in .NET 10. It is a rewrite of the Go
`gpsd-exporter`, with Prometheus-style metric names and fixes for the bugs found in the Go version.

Work in progress. Installation and the metric reference will be documented here as the exporter is built.

## Building

The SDK version is pinned in `global.json`.

```sh
dotnet build
dotnet test
```

`dotnet test` uses Microsoft Testing Platform (selected in `global.json`). Compiler, analyzer, trim and
Native AOT warnings fail the build, as do known-vulnerable NuGet packages.

To produce the native binary:

```sh
dotnet publish src/GpsdExporter -r linux-x64 -c Release
```

## Layout

| Path | Contents |
| -- | -- |
| `src/GpsdExporter` | The exporter (Native AOT, ASP.NET Core slim host). |
| `tests/GpsdExporter.Tests.Unit` | Parser, state store, staleness, backoff and configuration. No sockets or HTTP. |
| `tests/GpsdExporter.Tests.Metrics` | Exposition tests: the host in-process, `/metrics` scraped and parsed. |
| `tests/GpsdExporter.Tests.Integration` | The exporter against FakeGpsd, a scripted in-process TCP server. |
| `tests/GpsdExporter.Tests.Release` | Smoke tests for the published binary and image. Skipped unless `GPSD_EXPORTER_BINARY` is set. |
| `tests/GpsdExporter.Testing` | Shared test helpers: the fixture loader and FakeGpsd. |
| `tests/fixtures` | gpsd sessions as `.jsonl`, built from `gpsd_json(5)` and gpsd's regression logs. See its README. |

## Licence

Apache 2.0. See `LICENSE`.
