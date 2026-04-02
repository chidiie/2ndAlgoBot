# ─────────────────────────────────────────────────────────────────────────────
# AlgoBot Dockerfile — Multi-stage build
# Stage 1: Build the app
# Stage 2: Run it in minimal runtime image (no SDK bloat)
# ─────────────────────────────────────────────────────────────────────────────

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy project file first (layer cache — only re-runs restore if .csproj changes)
COPY AlgoBot/AlgoBot.csproj ./
RUN dotnet restore AlgoBot.csproj

# Copy remaining source
COPY AlgoBot/ ./

# Publish in Release mode — self-contained, trimmed, fast startup
RUN dotnet publish AlgoBot.csproj \
    -c Release \
    -o /publish \
    --no-restore

# ─────────────────────────────────────────────────────────────────────────────
# Stage 2: Runtime only — much smaller image
# ─────────────────────────────────────────────────────────────────────────────

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Add sqlite3 for database inspection
RUN apt-get update && apt-get install -y sqlite3 && rm -rf /var/lib/apt/lists/*

# Create directories for logs and database — mounted as volumes in production
RUN mkdir -p /app/logs /app/data

# Copy published output from build stage
COPY --from=build /publish ./

# The database and logs should live in volumes, not the container
# This means they survive container restarts and updates
VOLUME ["/app/logs", "/app/data"]

# Override appsettings with env vars using ALGOBOT_ prefix
# e.g. ALGOBOT_MetaApi__Token=xxx overrides MetaApi.Token
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "AlgoBot.dll"]
CMD []
