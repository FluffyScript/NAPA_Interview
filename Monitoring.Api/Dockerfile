# syntax=docker/dockerfile:1

# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer-cache friendly)
COPY Monitoring.Api/Monitoring.Api.csproj Monitoring.Api/
RUN dotnet restore Monitoring.Api/Monitoring.Api.csproj

# Copy everything else and publish
COPY . .
WORKDIR /src/Monitoring.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Monitoring.Api.dll"]
