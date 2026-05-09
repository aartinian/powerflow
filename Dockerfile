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
RUN dotnet publish PowerFlow.Web/PowerFlow.Web.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ─── Runtime stage ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as a non-root user. Fly's runtime is sandboxed already, but
# defense-in-depth costs nothing and matches Microsoft's own guidance.
RUN groupadd --system --gid 1000 app \
 && useradd  --system --uid 1000 --gid app app

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
