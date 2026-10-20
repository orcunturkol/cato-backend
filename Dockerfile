# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore (layer caching)
COPY src/Cato.Domain/Cato.Domain.csproj src/Cato.Domain/
COPY src/Cato.Infrastructure/Cato.Infrastructure.csproj src/Cato.Infrastructure/
COPY src/Cato.API/Cato.API.csproj src/Cato.API/
RUN dotnet restore src/Cato.API/Cato.API.csproj

# Copy everything and build
COPY src/ src/
RUN dotnet publish src/Cato.API/Cato.API.csproj -c Release -o /app/publish --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl for the container health check (the aspnet image has none).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

EXPOSE 8080

# Liveness only: /health/live runs no dependency checks. The long start period covers
# the Redis backfill, which runs before Kestrel starts listening.
HEALTHCHECK --interval=30s --timeout=5s --start-period=180s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Cato.API.dll"]
