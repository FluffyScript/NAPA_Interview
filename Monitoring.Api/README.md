A Prometheus integration for your .NET Minimal API usually looks like this:

your API publishes a /metrics endpoint
Prometheus scrapes that endpoint on an interval
Prometheus stores the time series
your frontend can either read from your own app endpoints or, more commonly, from Grafana backed by Prometheus. Prometheus is built around scrape_configs, where each job defines targets to scrape, and the .NET OpenTelemetry docs describe exposing metrics for Prometheus to pull from an HTTP endpoint.

For your case, I would keep the responsibilities separated:

your .NET app exposes business endpoints and monitoring-friendly metrics
Prometheus scrapes /metrics
PostgreSQL stats still come from your own code querying pg_stat_activity, pg_stat_statements, and similar views
CPU/RAM can come from app/runtime metrics, and later from a host exporter such as Node Exporter if you want machine-level visibility. Prometheus officially recommends Node Exporter for Linux host metrics like CPU and memory.
What the integration looks like

Inside the app, you instrument:

HTTP request duration/count
DB query duration/count
cache hits/misses
custom gauges or counters such as active monitoring checks
optional runtime/process metrics

Then you expose those metrics at /metrics, and Prometheus scrapes that endpoint every few seconds. The OpenTelemetry .NET docs describe Prometheus export as a pull model, and the Prometheus exporter spec says the exporter responds to HTTP requests with Prometheus-formatted metrics.

Recommended shape for your app

I would use:

ASP.NET Core Minimal API
OpenTelemetry Metrics
Prometheus exporter endpoint
Npgsql for PostgreSQL access
optional custom meters for DB monitoring queries

That gives you:

standard .NET instrumentation
a clean /metrics endpoint for Prometheus
room to add Grafana later without changing the app design. Microsoft and OpenTelemetry both document this pattern for ASP.NET Core metrics and Prometheus export.
Minimal API example

This is the shape I’d start with in Program.cs:

using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("PgMonitoring")
            .AddPrometheusExporter();
    });

var app = builder.Build();

var meter = new Meter("PgMonitoring");
var requestsChecked = meter.CreateCounter<long>("monitoring_checks_total");
var activeCollectors = meter.CreateObservableGauge(
    "monitoring_collectors_active",
    () => new Measurement<int>(1));

app.MapGet("/", () => "Monitoring API is running");

app.MapGet("/api/monitoring/overview", () =>
{
    requestsChecked.Add(1);

    return Results.Ok(new
    {
        status = "ok",
        timestamp = DateTimeOffset.UtcNow
    });
});

app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();

This follows the current OpenTelemetry .NET guidance: add metrics instrumentation, then expose a Prometheus scraping endpoint from the ASP.NET Core app.

A useful mental model is:

/api/... → for your frontend
/metrics → for Prometheus only

Do not make the frontend parse Prometheus metrics text directly unless you have a very specific reason.

Dockerfile for the .NET Minimal API

Here is a production-style Dockerfile for your app:

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY *.sln ./
COPY Monitoring.Api/Monitoring.Api.csproj Monitoring.Api/
RUN dotnet restore Monitoring.Api/Monitoring.Api.csproj

COPY . .
WORKDIR /src/Monitoring.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Monitoring.Api.dll"]

This is just your app container. Prometheus should run as a separate container, not inside the same image. That separation is the normal Prometheus deployment pattern. Prometheus itself is configured by its YAML config, especially scrape_configs.

Prometheus config

Create prometheus/prometheus.yml:

global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: "monitoring-api"
    static_configs:
      - targets: ["monitoring-api:8080"]

That tells Prometheus to scrape the monitoring-api service every 15 seconds. Prometheus documents scrape_configs and static_configs exactly for this kind of static target setup.

Docker Compose setup

Then wire both containers together with Compose:

services:
  monitoring-api:
    build:
      context: .
      dockerfile: Monitoring.Api/Dockerfile
    container_name: monitoring-api
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Postgres: Host=host.docker.internal;Port=5432;Database=mydb;Username=myuser;Password=mypassword

  prometheus:
    image: prom/prometheus:latest
    container_name: prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro
    depends_on:
      - monitoring-api

Compose starts services in dependency order with depends_on, which is useful here so Prometheus comes up after the API container is launched.

What metrics you should expose from the app

For your monitoring API, I would expose at least these custom metrics:

monitoring_checks_total counter
monitoring_check_duration_seconds histogram
postgres_long_running_queries gauge
postgres_blocked_sessions gauge
postgres_dead_tuples gauge with labels for schema/table
postgres_slow_query_count gauge

That way Prometheus can store time series like:

active blocked sessions over time
long-running query count over time
dead tuples trend per table
request latency of your API itself

OpenTelemetry .NET supports counters, gauges, and histograms through the .NET metrics API, and Prometheus can scrape them once exported through the Prometheus endpoint.

How the PostgreSQL part fits

Your app should still query PostgreSQL directly for database internals. A typical flow is:

a background service runs every 15–30 seconds
it queries pg_stat_activity, pg_stat_statements, pg_stat_user_tables
it updates in-memory values or records snapshots
your custom gauges expose the latest values to /metrics
your /api/monitoring/... endpoints return a frontend-friendly JSON view of the same data

This is often the cleanest design because Prometheus gets metrics, while your frontend gets structured JSON tailored to the dashboard.

A simple collector service might compute:

long-running query count
blocked session count
total active sessions
max query duration
top dead tuple table count

Then publish them as gauges.

Example background collector shape
using System.Diagnostics.Metrics;

public sealed class PostgresMonitoringCollector : BackgroundService
{
    private readonly Meter _meter = new("PgMonitoring");
    private readonly ObservableGauge<int> _longRunningGauge;
    private readonly ObservableGauge<int> _blockedGauge;

    private int _longRunningQueries;
    private int _blockedSessions;

    public PostgresMonitoringCollector()
    {
        _longRunningGauge = _meter.CreateObservableGauge(
            "postgres_long_running_queries",
            () => new Measurement<int>(_longRunningQueries));

        _blockedGauge = _meter.CreateObservableGauge(
            "postgres_blocked_sessions",
            () => new Measurement<int>(_blockedSessions));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Query Postgres here with Npgsql/Dapper.
            // Example:
            // _longRunningQueries = await repository.GetLongRunningQueryCountAsync();
            // _blockedSessions = await repository.GetBlockedSessionCountAsync();

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}

This is the bridge between PostgreSQL stats and Prometheus metrics.

CPU and RAM with Prometheus

There are two different scopes:

1. app/runtime metrics

With .NET runtime instrumentation, you can capture runtime/process-style metrics for the application process itself. That helps answer “is my API process healthy?” OpenTelemetry’s .NET metrics guidance includes runtime instrumentation as part of standard application observability.

2. machine metrics

For full server CPU and RAM, use Node Exporter on the host or VM. Prometheus officially documents Node Exporter as the standard way to expose Linux host hardware and kernel metrics.

If you later add Node Exporter, your Prometheus config becomes:

global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: "monitoring-api"
    static_configs:
      - targets: ["monitoring-api:8080"]

  - job_name: "node"
    static_configs:
      - targets: ["node-exporter:9100"]

That gives you both:

app metrics from your API
host CPU/RAM metrics from Node Exporter
What your frontend should consume

You have two clean options:

Option A

Frontend calls your .NET JSON endpoints only.

Use Prometheus only as storage/alerting behind the scenes.

This is best if:

you want total control over DTOs
you want business-friendly summaries
your frontend team does not want to learn PromQL
Option B

Frontend uses Grafana or queries Prometheus directly for charts.

This is best if:

you want ready-made dashboards
you are comfortable with PromQL
you want flexible historical charting fast

Most teams end up with both:

frontend uses your API for dashboard cards and detail views
Grafana uses Prometheus for charts and alerting
Good starter Prometheus queries

Once scraped, you can build things like:

request rate for your API
p95 API latency
long-running query count over time
blocked sessions over time
memory usage trends if Node Exporter is present

I would avoid putting PromQL into your frontend at first. Keep the frontend simple and let the backend or Grafana own that complexity.

My recommended starter stack

For your project, I would start with:

.NET Minimal API exposing /api/... and /metrics
Prometheus scraping the app
PostgreSQL queried directly by the app
optional Grafana later
optional Node Exporter later for host CPU/RAM

That gives you a clean first version without overcomplicating the architecture. Prometheus is designed to scrape targets on an interval, and OpenTelemetry .NET supports exposing a scrape endpoint in-process, so this is a natural fit.

If you want, I can turn this into a complete starter project structure with Program.cs, a sample collector service, docker-compose.yml, and prometheus.yml laid out exactly as files.

EDIT:
To add Prometheus monitoring to your ASP.NET Core project, you typically use the prometheus-net.AspNetCore package.

Run this command in your project directory:

dotnet add package prometheus-net.AspNetCore
Optional (but common related packages)

Depending on your needs, you might also add:

dotnet add package prometheus-net
dotnet add package prometheus-net.DotNetRuntime
Quick next step (after install)

In your Program.cs, you usually enable metrics like this:

app.UseRouting();

app.UseHttpMetrics(); // collects HTTP metrics

app.UseEndpoints(endpoints =>
{
    endpoints.MapMetrics(); // exposes /metrics endpoint
});

If you want, I can tailor the setup for your specific ASP.NET Core version (.NET 6/7/8/9) or show how to integrate it with Grafana.