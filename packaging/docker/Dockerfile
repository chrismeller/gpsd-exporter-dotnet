# The gpsd-exporter container image (R10). It holds only the Native AOT binary, built
# natively for each architecture by the binaries workflow, on the chiseled .NET runtime-deps
# base: glibc and the few libraries a .NET app needs, no shell or package manager.
#
# The build context has the binaries at <TARGETPLATFORM>/gpsd-exporter, for example
# linux/amd64/gpsd-exporter and linux/arm64/gpsd-exporter. There are no RUN steps, so buildx
# assembles both architectures without emulation.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled@sha256:40eba5db69594d517c3a61e96bdacc2e3d6f8c99a44b52d55edee844d33518d0

ARG TARGETPLATFORM
ARG VERSION=0.0.0-dev

LABEL org.opencontainers.image.title="gpsd-exporter" \
      org.opencontainers.image.description="Prometheus exporter for gpsd" \
      org.opencontainers.image.source="https://github.com/chrismeller/gpsd-exporter-dotnet" \
      org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.version="${VERSION}"

COPY --chmod=0555 ${TARGETPLATFORM}/gpsd-exporter /app/gpsd-exporter

# AC10.2: the base image's unprivileged app user.
USER $APP_UID

EXPOSE 9978
ENTRYPOINT ["/app/gpsd-exporter"]
