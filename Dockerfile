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

EXPOSE 8080

ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Cato.API.dll"]
