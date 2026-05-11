# syntax=docker/dockerfile:1
#
# PowerFlow.Web — production container for Fly.io.
#
# Multi-stage build:
#   1. Pull the .NET 10 SDK, restore Web + Core (Tests/Runner/Samples skipped).
#   2. Publish PowerFlow.Web against the ASP.NET runtime image.
#
# The split keeps the final image small (~250 MB vs ~1 GB for the SDK image)
# and lets Docker cache `dotnet restore` until a *.csproj actually changes.

# ─── Build stage ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first so `restore` is cache-friendly. Layer is
# only invalidated when one of these *.csproj files changes — not when
# any source file under the project changes.
COPY PowerFlow.Core/PowerFlow.Core.csproj  PowerFlow.Core/
COPY PowerFlow.Web/PowerFlow.Web.csproj    PowerFlow.Web/

# Restore from the Web project; transitively pulls Core. Tests, Runner,
# and Samples are deliberately not copied — they are not part of the
# deployable surface and would slow the build with extra restore work.
RUN dotnet restore PowerFlow.Web/PowerFlow.Web.csproj

# Now copy source. This layer is invalidated on every code change, but
# `restore` above stays cached.
COPY PowerFlow.Core/  PowerFlow.Core/
COPY PowerFlow.Web/   PowerFlow.Web/

# Publish for production. UseAppHost=false skips the platform-specific
# launcher executable since we run via `dotnet PowerFlow.Web.dll` instead.
#
# NOTE: do NOT add --no-restore here. Blazor's static-web-asset targets
# (the ones that copy _framework/blazor.web.js into the publish output)
# require publish to run its own restore phase to wire up MSBuild item
# groups correctly. --no-restore skips that wiring and the _framework/
# directory is silently omitted, making the deployed app load a blank
# page (blazor.web.js 404 → no Blazor circuit → no interactivity).
# The earlier `dotnet restore` layer already populated the NuGet global
# cache, so this restore is a fast cache-hit, not a network download.
RUN dotnet publish PowerFlow.Web/PowerFlow.Web.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false && \
    # Guard: fail loudly if the framework JS is still missing rather than
    # silently shipping a broken image.
    test -f /app/publish/wwwroot/_framework/blazor.web.js || \
        { echo "ERROR: blazor.web.js missing from publish output"; exit 1; }

# ─── Runtime stage ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet runtime image already provides a non-root `app` user (UID
# 1000) since .NET 8 — recreating it collides with `useradd: UID already
# exists` (exit 9). Just chown the copy and switch users.
COPY --chown=app:app --from=build /app/publish ./
USER app

# Listen on the port Fly's edge proxy forwards to (matches fly.toml's
# internal_port). Production environment turns on production logging.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "PowerFlow.Web.dll"]
