# gpsd-exporter-dotnet

A Prometheus exporter for [gpsd](https://gpsd.io/), written in .NET 10. It is a rewrite of the Go
`gpsd-exporter`, with Prometheus-style metric names and fixes for the bugs found in the Go version.

Work in progress. Installation and the metric reference will be documented here as the exporter is built.

## Installing the binary

Each release has a single self-contained executable for `linux-x64` and `linux-arm64` (64-bit
Raspberry Pi OS included). It needs glibc 2.36 or newer (Debian 12, Raspberry Pi OS bookworm, Ubuntu
22.04 and later) and nothing else; no .NET install is required. It takes the Go exporter's flags, so it
can replace it in place.

```sh
arch=arm64   # or x64
base=https://github.com/chrismeller/gpsd-exporter-dotnet/releases/latest/download
curl -fsSLO "$base/gpsd-exporter-linux-$arch"
curl -fsSLO "$base/gpsd-exporter-linux-$arch.sha256"
sha256sum --check "gpsd-exporter-linux-$arch.sha256"
sudo install -m 0755 "gpsd-exporter-linux-$arch" /usr/local/bin/gpsd-exporter
gpsd-exporter --version
```

### Running it with systemd

The release also has `gpsd-exporter.service` (from `packaging/systemd`). It starts after gpsd, restarts
on failure (but not on a configuration error, exit code 2), and runs as a transient unprivileged user
(`DynamicUser=yes`) with a read-only view of the system.

```sh
curl -fsSLO "$base/gpsd-exporter.service"
curl -fsSLO "$base/gpsd-exporter.env"
sudo install -m 0644 gpsd-exporter.service /etc/systemd/system/
sudo install -m 0644 gpsd-exporter.env /etc/default/gpsd-exporter   # optional; every line is commented out
sudo systemctl daemon-reload
sudo systemctl enable --now gpsd-exporter
curl -s localhost:9978/metrics | grep gpsd_up
```

Settings go in `/etc/default/gpsd-exporter` as `GPSD_EXPORTER_` variables, the section and key joined by a
double underscore, for example `GPSD_EXPORTER_GPSD__ADDRESS=localhost:2947` or
`GPSD_EXPORTER_HTTP__LISTEN=127.0.0.1:9978`. The example file lists the common ones with their
defaults. Run `sudo systemctl restart gpsd-exporter` after changing it, and `journalctl -u gpsd-exporter`
to see the logs.

## Running the container image

`ghcr.io/chrismeller/gpsd-exporter-dotnet` is published for `linux/amd64` and `linux/arm64`. It holds
only the native binary on Microsoft's chiseled `runtime-deps` base (no shell or package manager) and
runs as an unprivileged user.

gpsd usually listens only on localhost, so the simplest setup shares the host's network. The exporter
then reaches gpsd at its default `localhost:2947` and serves `/metrics` on port 9978:

<!-- readme-example: docker -->
```sh
docker run -d --name gpsd-exporter --restart unless-stopped --network host \
  ghcr.io/chrismeller/gpsd-exporter-dotnet:latest
```

If gpsd is elsewhere, or listens on an address the container can reach, use a normal network and pass
its address. Flags and `GPSD_EXPORTER_` variables work as they do for the binary:

```sh
docker run -d --name gpsd-exporter --restart unless-stopped -p 9978:9978 \
  -e GPSD_EXPORTER_GPSD__ADDRESS=gps-host.example:2947 \
  ghcr.io/chrismeller/gpsd-exporter-dotnet:latest
```

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

Exposition tests use [Verify](https://github.com/VerifyTests/Verify) for snapshots. Approved snapshots are
`*.verified.txt` files under `tests/GpsdExporter.Tests.Metrics/Snapshots`; a mismatch writes a `.received.txt`
next to it (ignored by git). Verify's licence check is satisfied by its OpenSource exemption in
`Directory.Build.props`, which must be renewed before `Verify_SponsorshipExemptionUntil`.

## Releasing

Push a tag `vX.Y.Z` from the commit to release. The release workflow builds both binaries with that
version, checks that `--version` and `gpsd_exporter_build_info` report it, and tests the image on both
architectures. Only then does it push `ghcr.io/chrismeller/gpsd-exporter-dotnet:X.Y.Z` and `:latest` and
create the GitHub release with the binaries, their checksums, `gpsd-exporter.service` and
`gpsd-exporter.env`.

```sh
git tag -a v1.0.0 -m "gpsd-exporter 1.0.0"
git push origin v1.0.0
```

To try a release without publishing anything, run the Release workflow by hand (Actions, Release, Run
workflow) with a test version. It does everything except publish, and leaves the release files as the
`release-files` artifact. It also runs this way on any pull request that changes the workflow.

## Layout

| Path | Contents |
| -- | -- |
| `src/GpsdExporter` | The exporter (Native AOT, ASP.NET Core slim host). |
| `tests/GpsdExporter.Tests.Unit` | Parser, state store, staleness, backoff and configuration. No sockets or HTTP. |
| `tests/GpsdExporter.Tests.Metrics` | Exposition tests: the host in-process, `/metrics` scraped and parsed. |
| `tests/GpsdExporter.Tests.Integration` | The exporter against FakeGpsd, a scripted in-process TCP server. |
| `tests/GpsdExporter.Tests.Release` | Smoke tests for the published binary and image. Skipped unless `GPSD_EXPORTER_BINARY` is set. |
| `tests/GpsdExporter.Testing` | Shared test helpers: the fixture loader, FakeGpsd, and the Prometheus text parser and snapshot normalizer. |
| `packaging/systemd` | The systemd unit and its example settings file. |
| `packaging/docker` | The container image's Dockerfile. |
| `scripts` | CI helpers: the glibc version check, the systemd unit check, the image build context and the release version check. |
| `tests/fixtures` | gpsd sessions as `.jsonl`, built from `gpsd_json(5)` and gpsd's regression logs. See its README. |

## Licence

Apache 2.0. See `LICENSE`.
