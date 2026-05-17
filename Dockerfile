# syntax=docker/dockerfile:1
#
# PowerFlow.Api — production container for Fly.io.
#
# Three stages:
#   1. Build the Vite SPA, output to PowerFlow.Api/wwwroot.
#   2. Restore + publish PowerFlow.Api against the ASP.NET runtime image.
#      The wwwroot from stage 1 is copied in before publish so the SPA
#      assets ride along into the final image.
#   3. Slim runtime image — only the publish output is copied across.

# ─── Stage 1: SPA bundle ───────────────────────────────────────────
FROM node:20-alpine AS spa
WORKDIR /src

# Lockfile-only copy first so `npm ci` is cache-friendly. The cache layer
# survives any source change inside PowerFlow.Client.
COPY PowerFlow.Client/package.json PowerFlow.Client/package-lock.json PowerFlow.Client/
RUN cd PowerFlow.Client && npm ci

COPY PowerFlow.Client/ PowerFlow.Client/

# vite.config.ts writes to ../PowerFlow.Api/wwwroot — preserved as-is so
# local and CI builds use the same output path. We just need the parent
# directory to exist inside the spa stage.
RUN mkdir -p PowerFlow.Api && \
    cd PowerFlow.Client && npm run build

# ─── Stage 2: .NET publish ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first for restore-cache friendliness.
COPY PowerFlow.Core/PowerFlow.Core.csproj  PowerFlow.Core/
COPY PowerFlow.Api/PowerFlow.Api.csproj    PowerFlow.Api/

RUN dotnet restore PowerFlow.Api/PowerFlow.Api.csproj

# Source + SPA bundle.
COPY PowerFlow.Core/ PowerFlow.Core/
COPY PowerFlow.Api/  PowerFlow.Api/
COPY --from=spa /src/PowerFlow.Api/wwwroot/ PowerFlow.Api/wwwroot/

# UseAppHost=false skips the platform-specific launcher; we run with
# `dotnet PowerFlow.Api.dll` directly.
RUN dotnet publish PowerFlow.Api/PowerFlow.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false && \
    # Guard: fail loudly if the SPA bundle is missing rather than ship
    # an API with no UI on top.
    test -f /app/publish/wwwroot/index.html || \
        { echo "ERROR: SPA bundle missing from publish output"; exit 1; }

# ─── Stage 3: Runtime ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet runtime image ships with a non-root `app` user (UID 1000)
# since .NET 8 — just chown the copy and switch.
COPY --chown=app:app --from=build /app/publish ./
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "PowerFlow.Api.dll"]
