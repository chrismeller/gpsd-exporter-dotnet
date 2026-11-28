# Changelog

## [2.1.0](https://github.com/chrismeller/gpsd-exporter-dotnet/compare/v2.0.0...v2.1.0) (2026-11-28)


### Features

* send traces over OTLP and restore ASP.NET Core metrics ([#57](https://github.com/chrismeller/gpsd-exporter-dotnet/issues/57)) ([821f0df](https://github.com/chrismeller/gpsd-exporter-dotnet/commit/821f0df60f1d04db76fa098a54bd0f9723dd4caf))

## [2.0.0](https://github.com/chrismeller/gpsd-exporter-dotnet/compare/v1.0.0...v2.0.0) (2026-11-24)


### ⚠ BREAKING CHANGES

* OTLP is now configured with the standard OTEL_* environment variables (OTEL_EXPORTER_OTLP_ENDPOINT replaces Otlp:Endpoint), the log format setting moved to Logging:Console:FormatterName, .NET runtime and process metrics are no longer served on /metrics (they go over OTLP only), and /healthz answers "Healthy" to any method. The old settings are ignored.

### Features

* use Microsoft health checks with a gpsd readiness check ([#52](https://github.com/chrismeller/gpsd-exporter-dotnet/issues/52)) ([561fdfa](https://github.com/chrismeller/gpsd-exporter-dotnet/commit/561fdfadd98c10925596e0909d24bd6461481852))


### Bug Fixes

* **deps:** update mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled docker digest to 2bb1d48 ([#46](https://github.com/chrismeller/gpsd-exporter-dotnet/issues/46)) ([d3ff661](https://github.com/chrismeller/gpsd-exporter-dotnet/commit/d3ff661b69fbce6b56f92dbb76f63bb5d7cadd6d))
* send the exporter's own logs and metrics over OTLP ([#53](https://github.com/chrismeller/gpsd-exporter-dotnet/issues/53)) ([bd9cf4f](https://github.com/chrismeller/gpsd-exporter-dotnet/commit/bd9cf4f8088fe09ef9fa43dfb220d782ae87151e))


### Code Refactoring

* remove the old host and ship the Aspire-based exporter ([#55](https://github.com/chrismeller/gpsd-exporter-dotnet/issues/55)) ([80f1f1a](https://github.com/chrismeller/gpsd-exporter-dotnet/commit/80f1f1a1503d625e007966b2556eea97d2483906))

## 1.0.0 (2026-11-23)


### Features

* manage releases with Release Please ([#33](https://github.com/chrismeller/gpsd-exporter-dotnet/issues/33)) ([a35701f](https://github.com/chrismeller/gpsd-exporter-dotnet/commit/a35701fd846167b6916fb98765009866a248b670))
