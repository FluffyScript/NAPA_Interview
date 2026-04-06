# Monitoring API — POC

A .NET 10 Minimal API that monitors a PostgreSQL database and exposes metrics for Prometheus to scrape. Built as a proof of concept for a NAPA interview.

---

## Architecture

```
┌─────────────┐     JSON      ┌──────────────┐
│   Frontend  │ ◄──────────── │  .NET API    │
└─────────────┘               │  :8080       │
                               │              │
┌─────────────┐   /metrics    │  /api/...    │
│ Prometheus  │ ◄──────────── │  /metrics    │
│  :9090      │               └──────┬───────┘
└──────┬──────┘                      │ pg_stat_*
       │                             ▼
┌──────▼──────┐               ┌──────────────┐
│   Grafana   │               │  PostgreSQL  │
│  :3000      │               │  :5432       │
└─────────────┘               └──────────────┘
```

**Separation of concerns:**
- `/api/monitoring/...` → structured JSON for the frontend
- `/metrics` → Prometheus scrape endpoint only, never for the frontend
- PostgreSQL stats are queried directly via `pg_stat_activity`, `pg_stat_statements`, `pg_stat_user_tables`

---

## Stack

| Concern | Choice |
|---|---|
| Framework | ASP.NET Core Minimal API (.NET 10) |
| Prometheus metrics | `prometheus-net.AspNetCore` + `prometheus-net.DotNetRuntime` |
| OpenAPI spec | `Microsoft.AspNetCore.OpenApi` (built-in .NET 10) |
| Swagger UI | `Swashbuckle.AspNetCore.SwaggerUI` at `/swagger` |
| PostgreSQL access | `Npgsql` + `Dapper` (raw SQL against `pg_stat_*` views) |
| Error responses | RFC 7807 Problem Details (`AddProblemDetails`) |
| Containerisation | Docker + Docker Compose |

---

## Project structure

```
.
├── CLAUDE.md                              # AI-assistant context and dev notes
├── Monitoring.sln
├── global.json                            # pins .NET 10 SDK
├── docker-compose.yml                     # api + prometheus + grafana + postgres
├── .dockerignore
├── Monitoring.Api/
│   ├── Monitoring.Api.csproj
│   ├── Program.cs                         # DI wiring, middleware, top-level routes
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Dockerfile                         # multi-stage, non-root user
│   ├── Models/
│   │   └── MonitoringModels.cs            # response DTOs (C# records)
│   ├── Repositories/
│   │   └── PostgresRepository.cs          # all pg_stat_* queries via Dapper
│   ├── Routes/
│   │   └── MonitoringRoutes.cs            # route definitions as extension method
│   └── Services/
│       └── PostgresMonitoringCollector.cs # BackgroundService → prometheus-net gauges
├── prometheus/
│   └── prometheus.yml                     # scrape config (15s interval)
└── postgres/
    └── init.sql                           # pg_stat_statements + sample schema
```

---

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (local or via Docker Compose)

> **Note:** `dotnet` may not be on PATH on Windows. Use the full path:
> `C:\Program Files\dotnet\dotnet.exe`

### Run locally

```bash
# 1. Set the connection string in appsettings.json, or override via environment:
#    ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=mydb;Username=myuser;Password=mypassword

# 2. Run
dotnet run --project Monitoring.Api/Monitoring.Api.csproj
```

| URL | Purpose |
|---|---|
| `http://localhost:8080/swagger` | Swagger UI |
| `http://localhost:8080/metrics` | Prometheus scrape endpoint |
| `http://localhost:8080/api/monitoring/overview` | JSON health snapshot |

### Run with Docker Compose

```bash
docker compose up --build
```

| Service | URL |
|---|---|
| API | `http://localhost:8080` |
| Prometheus | `http://localhost:9090` |
| Grafana | `http://localhost:3000` (admin / admin) |
| PostgreSQL | `localhost:5432` |

Postgres healthcheck gates the API startup — the API won't start until Postgres is ready.

---

## API endpoints

All endpoints return `application/json`. When the database is unreachable, all endpoints return `503 Service Unavailable` in Problem Details format (RFC 7807).

### `GET /api/monitoring/overview`

Top-level snapshot for dashboard cards.

```json
{
  "longRunningQueryCount": 2,
  "blockedSessionCount": 0,
  "totalActiveSessions": 14,
  "maxQueryDurationSeconds": 47.3,
  "timestamp": "2026-04-02T10:00:00Z"
}
```

### `GET /api/monitoring/long-running?thresholdSeconds=30`

Queries currently running longer than the threshold (default 30 s), ordered by duration descending.

```json
[
  {
    "pid": 1234,
    "state": "active",
    "query": "SELECT ...",
    "durationSeconds": 47.3,
    "applicationName": "my-app"
  }
]
```

### `GET /api/monitoring/blocked`

Sessions currently blocked by a lock, paired with the blocking session.

```json
[
  {
    "blockedPid": 1234,
    "blockedQuery": "UPDATE orders ...",
    "blockingPid": 5678,
    "blockingQuery": "UPDATE orders ..."
  }
]
```

### `GET /api/monitoring/dead-tuples?minDeadTuples=100`

User tables with at least `minDeadTuples` dead tuples (default 100), ordered by count descending. Useful for identifying tables that need `VACUUM`.

```json
[
  {
    "schemaName": "public",
    "tableName": "orders",
    "deadTupleCount": 84201,
    "liveTupleCount": 500000
  }
]
```

### Error response (503)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.4",
  "title": "Database Unavailable",
  "status": 503,
  "detail": "Unable to reach the database. Please try again later."
}
```

---

## Prometheus metrics

The background collector (`PostgresMonitoringCollector`) runs every 15 s and updates these gauges. All are exposed at `/metrics`.

### Custom PostgreSQL metrics

| Metric | Type | Description |
|---|---|---|
| `postgres_long_running_queries` | Gauge | Queries running > 30 s |
| `postgres_blocked_sessions` | Gauge | Sessions blocked by a lock |
| `postgres_active_sessions` | Gauge | Total active sessions |
| `postgres_slow_query_count` | Gauge | Queries with mean exec time > 1 s¹ |
| `postgres_dead_tuples{schema, table}` | Gauge | Dead tuple count per table (labeled) |
| `monitoring_collections_total` | Counter | Collection cycles completed |
| `monitoring_collection_duration_seconds` | Histogram | Time per collection cycle |

¹ Requires the `pg_stat_statements` extension. Gracefully returns 0 if absent.

### Auto-instrumented metrics

| Source | Metrics |
|---|---|
| `UseHttpMetrics()` | `http_requests_*` — request count, duration, in-flight |
| `DotNetRuntimeStatsBuilder` | `dotnet_*` — GC, threadpool, contention, exceptions |

### Starter PromQL queries

```promql
# Request rate
rate(http_requests_received_total[1m])

# p95 API latency
histogram_quantile(0.95, rate(http_request_duration_seconds_bucket[5m]))

# Long-running queries over time
postgres_long_running_queries

# Blocked sessions over time
postgres_blocked_sessions

# Dead tuples for a specific table
postgres_dead_tuples{schema="public", table="orders"}
```

---

## Key design decisions

**`prometheus-net` over OpenTelemetry exporter**
The original spec used the OpenTelemetry Prometheus exporter. The README's EDIT section explicitly called for `prometheus-net.AspNetCore`, which offers native Prometheus semantics — labeled gauges, `UseHttpMetrics()`, `.NewTimer()` histograms — without the OTel translation layer.

**Labeled dead-tuple gauge**
`postgres_dead_tuples{schema, table}` gives one time-series per table, enabling per-table alerting rules and Grafana panels.

**Background collector with concurrent queries**
`PostgresMonitoringCollector` fires all `pg_stat_*` queries with `Task.WhenAll` to minimise wall-clock time per cycle. If the DB is down, it logs the error and retries on the next tick — the app stays running.

**Routes as extension methods**
All monitoring routes live in `MonitoringRoutes.cs` as a `MapMonitoringRoutes()` extension on `IEndpointRouteBuilder`, grouped under `/api/monitoring`. `Program.cs` stays as pure wiring.

**Problem Details for error responses**
`AddProblemDetails` with a custom `CustomizeProblemDetails` callback maps `NpgsqlException` → `503`. All other unhandled exceptions fall through as `500`. Both cases return RFC 7807 JSON.

---

## Extending

**Add Node Exporter for host CPU/RAM**

```yaml
# prometheus/prometheus.yml
scrape_configs:
  - job_name: "monitoring-api"
    static_configs:
      - targets: ["monitoring-api:8080"]
  - job_name: "node"
    static_configs:
      - targets: ["node-exporter:9100"]
```

**Add Grafana dashboards**

Grafana is already in the Compose stack at `http://localhost:3000`. Add Prometheus as a data source (`http://prometheus:9090`) and build panels using the PromQL queries above.

**Add a new monitoring endpoint**

1. Add a query method to `PostgresRepository`
2. Add the route to `Routes/MonitoringRoutes.cs`
3. Add a gauge/counter to `Services/PostgresMonitoringCollector.cs` if Prometheus tracking is needed
"# NAPA_Interview" 
