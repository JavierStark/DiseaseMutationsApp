# syntax=docker/dockerfile:1.7
# Registry/local tag of the index base image (see Dockerfile.bowtie-base). Override with --build-arg BOWTIE_BASE=...
ARG BOWTIE_BASE=disease-mutations-bowtie:grch38-noalt-20260526

# ---- Build stage -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0.306-bookworm-slim@sha256:81f6d622fe21ed9d31375167f62a3538ff4d6835f9d5e6da9c2defa8a84b7687 AS build
WORKDIR /src

# Restore first, from project files + lock files only, so the layer caches. --locked-mode fails on any drift.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY gRNA/gRNA.fsproj gRNA/packages.lock.json gRNA/
COPY DiseaseMutationsApp/DiseaseMutationsApp.csproj DiseaseMutationsApp/packages.lock.json DiseaseMutationsApp/
COPY gRNA.Cli/gRNA.Cli.fsproj gRNA.Cli/packages.lock.json gRNA.Cli/
RUN dotnet restore --locked-mode DiseaseMutationsApp/DiseaseMutationsApp.csproj && \
    dotnet restore --locked-mode gRNA.Cli/gRNA.Cli.fsproj

COPY gRNA/ gRNA/
COPY DiseaseMutationsApp/ DiseaseMutationsApp/
COPY gRNA.Cli/ gRNA.Cli/
RUN dotnet publish DiseaseMutationsApp/DiseaseMutationsApp.csproj -c Release --no-restore -o /out/app /p:UseAppHost=false && \
    dotnet publish gRNA.Cli/gRNA.Cli.fsproj -c Release --no-restore -o /out/cli /p:UseAppHost=false

# ---- Final image: index base + Python/ViennaRNA + app --------------------------------------------
FROM ${BOWTIE_BASE} AS final
WORKDIR /app

# ViennaRNA is pinned: the fold column depends on its Python binding. Version 2.7.2 returns a list,
# e.g. ['((((....))))', -5.1]; the library no longer depends on that repr (see docs/runtime-contract.md).
ARG VIENNARNA_VERSION=2.7.2
RUN --mount=type=cache,target=/root/.cache/pip \
    apt-get update && \
    apt-get install -y --no-install-recommends python3 python3-pip && \
    pip3 install --break-system-packages "viennarna==${VIENNARNA_VERSION}" && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build --chown=app:app /out/app ./
COPY --from=build --chown=app:app /out/cli /opt/grna-cli/

# Bowtie 1.3.1 (bowtie-align-s); the checksum matches the upstream bowtie-1.3.1-linux-x86_64 release.
COPY --chown=app:app bowtie/bowtie-align-s /app/bowtie/bowtie-align-s
RUN echo "45c58c69a10577e84e459e6b76d6c8f17ab030a5510d4b6399e6434228a9eaca  /app/bowtie/bowtie-align-s" | sha256sum -c - && \
    chmod 0755 /app/bowtie/bowtie-align-s && \
    printf '#!/bin/sh\nexec dotnet /opt/grna-cli/gRNA.Cli.dll "$@"\n' > /usr/local/bin/grna && \
    chmod 0755 /usr/local/bin/grna

ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    GRNA_PYTHON=python3

USER app
EXPOSE 5000

# /healthz runs the same preflight checks as `grna doctor` (Bowtie binary + index, Python + ViennaRNA).
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD ["python3", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:5000/healthz', timeout=8)"]

ENTRYPOINT ["dotnet", "DiseaseMutationsApp.dll"]
