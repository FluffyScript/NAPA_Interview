# CLAUDE.md — Monitoring API

## Project purpose
.NET 10 Minimal API that monitors a PostgreSQL database and exposes metrics for Prometheus to scrape. It is a POC for an interview at NAPA.

## How to build & run

```bash
# Build
"C:\Program Files\dotnet\dotnet.exe" build Monitoring.Api/Monitoring.Api.csproj

# Run (needs a reachable Postgres; see connection string below)
"C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj
```

`dotnet` is NOT on PATH on this machine. Use the full path `C:\Program Files\dotnet\dotnet.exe`.

Useful URLs once running:
- `http://localhost:8080/swagger` — Swagger UI
- `http://localhost:8080/metrics` — Prometheus scrape endpoint
- `http://localhost:8080/api/monitoring/overview` — JSON health snapshot

## Stack

| Concern | Library |
|---|---|
| Framework | ASP.NET Core Minimal API (.NET 10) |
| Prometheus metrics | `prometheus-net.AspNetCore` + `prometheus-net.DotNetRuntime` |
| OpenAPI spec | `Microsoft.AspNetCore.OpenApi` (built-in .NET 10) |
| Swagger UI | `Swashbuckle.AspNetCore.SwaggerUI` served at `/swagger` |
| PostgreSQL access | `Npgsql` + `Dapper` |

## Project layout

```
Monitoring.Api/
  Program.cs                          — wiring: DI, middleware, routes
  Monitoring.Api.csproj
  appsettings.json                    — connection string, collection interval
  Models/MonitoringModels.cs          — response DTOs (C# records)
  Repositories/PostgresRepository.cs  — all pg_stat_* queries via Dapper
  Services/PostgresMonitoringCollector.cs  — BackgroundService → prometheus-net gauges
  Dockerfile                          — multi-stage, non-root, .NET 10
prometheus/prometheus.yml             — scrape config
postgres/init.sql                     — pg_stat_statements + sample schema
docker-compose.yml                    — api + prometheus + grafana + postgres
```

## Metrics exposed at /metrics

| Metric | Type | Description |
|---|---|---|
| `postgres_long_running_queries` | Gauge | Queries running > 30 s |
| `postgres_blocked_sessions` | Gauge | Sessions blocked by a lock |
| `postgres_active_sessions` | Gauge | Total active sessions |
| `postgres_slow_query_count` | Gauge | Queries with mean exec time > 1 s (needs `pg_stat_statements`) |
| `postgres_dead_tuples{schema, table}` | Gauge | Dead tuple count per table (labeled) |
| `monitoring_collections_total` | Counter | Collection cycles completed |
| `monitoring_collection_duration_seconds` | Histogram | Time per collection cycle |
| `http_requests_*` | auto | HTTP request metrics from `UseHttpMetrics()` |
| `dotnet_*` | auto | .NET runtime metrics from `DotNetRuntimeStatsBuilder` |

## Connection string

Set in `appsettings.json` under `ConnectionStrings:Postgres`.  
Override via environment variable: `ConnectionStrings__Postgres`.  
Docker Compose sets it to `Host=postgres;Port=5432;Database=mydb;Username=myuser;Password=mypassword`.

## Key design decisions

- **prometheus-net over OpenTelemetry exporter**: README EDIT section explicitly calls for `prometheus-net.AspNetCore`. It offers native Prometheus semantics — labeled gauges, `UseHttpMetrics()`, `.NewTimer()` histograms — without the OTel translation layer.
- **Labeled dead-tuple gauge**: `postgres_dead_tuples{schema, table}` gives one time-series per table, enabling per-table alerting rules in Prometheus.
- **Background collector runs concurrent queries**: `Task.WhenAll` across all `pg_stat_*` queries to minimise wall-clock time per cycle.
- **`/metrics` is not for the frontend**: JSON endpoints under `/api/monitoring/` are for the UI; `/metrics` is for Prometheus only.
- **Swagger UI is always on**: not gated by environment, intentional for this POC.

## Docker (not yet configured on dev machine)

```bash
docker compose up --build
```

Services: `monitoring-api` (8080), `prometheus` (9090), `grafana` (3000), `postgres` (5432).  
Postgres healthcheck gates the API startup via `depends_on.condition: service_healthy`.

## pg_stat_statements

The `postgres_slow_query_count` metric requires the `pg_stat_statements` extension.  
`postgres/init.sql` enables it automatically in the Docker Compose Postgres container.  
If absent, the query gracefully returns 0 (catches `PostgresException` with `SqlState == "42P01"`).
