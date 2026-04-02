# Session Notes — NAPA Interview POC

Last updated: 2026-04-02  
Model: Claude Sonnet 4.6  
Git HEAD: `5f7b588`

---

## What this project is

.NET 10 Minimal API that monitors a PostgreSQL database and exposes metrics for Prometheus to scrape.
Built as a POC for a NAPA interview. Everything is intentionally clean and "interview-worthy."

---

## Current state — FULLY BUILT, builds clean (0 errors, 0 warnings)

### Endpoints

| Method | URL | Returns |
|--------|-----|---------|
| GET | `/api/monitoring/overview` | DB session snapshot (active, blocked, long-running, max duration) |
| GET | `/api/monitoring/long-running?thresholdSeconds=30` | Queries running > N seconds |
| GET | `/api/monitoring/blocked` | Blocked/blocking session pairs |
| GET | `/api/monitoring/dead-tuples?minDeadTuples=100` | Tables with dead tuple accumulation |
| GET | `/api/monitoring/system` | Process CPU %, RAM (human-readable), system RAM on Linux |
| GET | `/metrics` | Prometheus scrape endpoint |
| GET | `/swagger` | Swagger UI |

### Services running in background

- `PostgresMonitoringCollector` — polls `pg_stat_*` every 15 s, updates prometheus-net Gauges
- `SystemMetricsService` — samples process CPU/RAM every 5 s, updates Gauges + stores snapshot

### Architecture layers (strict — enforced by CLAUDE.md)

```
Routes → Repository → DbContext → PostgreSQL
                ↑
  (BackgroundService goes through Repository too)
```

SQL lives only in `DbContext.OnModelCreating` via `ToSqlQuery()`.  
Repositories use pure LINQ only.  
All string literals centralized in `Constants.cs`.

---

## How to run

`dotnet` is NOT on PATH. Always use full path:

```bash
# Build
"C:\Program Files\dotnet\dotnet.exe" build Monitoring.Api/Monitoring.Api.csproj

# Run (Mode A: local API + Docker DB)
docker compose -f docker-compose.db-only.yml up -d
"C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj
```

API listens on:
- http://localhost:64965  (HTTP)
- https://localhost:64964 (HTTPS)

Connection string in `appsettings.json` → `ConnectionStrings:Postgres`  
Password contains `$` chars — if passing via Docker env, escape as `$$` in YAML.

See `startme.md` for full switching guide.

---

## Key files

| File | Purpose |
|------|---------|
| `Monitoring.Api/Constants.cs` | Every named string — routes, metrics, config keys, errors |
| `Monitoring.Api/Data/MonitoringDbContext.cs` | All SQL via `ToSqlQuery()`; entity mapping |
| `Monitoring.Api/Repositories/PostgresRepository.cs` | Pure LINQ; no SQL strings |
| `Monitoring.Api/Services/PostgresMonitoringCollector.cs` | Postgres metric background poller |
| `Monitoring.Api/Services/SystemMetricsService.cs` | CPU/RAM sampler; cross-platform |
| `Monitoring.Api/Routes/MonitoringRoutes.cs` | HTTP endpoint definitions |
| `Monitoring.Api/Models/MonitoringModels.cs` | All DTO records |
| `CLAUDE.md` | Code of conduct (10 rules) + build/run instructions |
| `CONVERSATION_LOG.md` | Full step-by-step history of what was built |

---

## Packages

```
prometheus-net.AspNetCore        8.2.1
prometheus-net.DotNetRuntime     4.4.1
Microsoft.AspNetCore.OpenApi     10.0.0
Swashbuckle.AspNetCore.SwaggerUI 7.2.0
Npgsql.EntityFrameworkCore.PostgreSQL  9.0.4
EFCore.NamingConventions         9.0.0
Microsoft.EntityFrameworkCore.Design   9.0.4
```

---

## Prometheus metrics exposed

| Metric | Type | Notes |
|--------|------|-------|
| `postgres_long_running_queries` | Gauge | Queries > 30 s |
| `postgres_blocked_sessions` | Gauge | |
| `postgres_active_sessions` | Gauge | |
| `postgres_slow_query_count` | Gauge | Requires `pg_stat_statements` extension |
| `postgres_dead_tuples` | Gauge | Labels: `schema`, `table` |
| `monitoring_collections_total` | Counter | DB collection cycles |
| `monitoring_collection_duration_seconds` | Histogram | Cycle timing |
| `process_cpu_usage_percent` | Gauge | 5-second rolling average |
| `process_memory_bytes` | Gauge | Process working-set (RSS) |
| `system_memory_total_bytes` | Gauge | Installed RAM |
| `system_memory_available_bytes` | Gauge | Linux only |
| + all `dotnet_*` / `http_*` | — | prometheus-net.DotNetRuntime + UseHttpMetrics() |

---

## Things that could be extended (not requested, just ideas)

- EF Core migration for the `orders` table has never been run — `init.sql` creates it directly.
- Windows system available RAM currently returns `null` — would need `GlobalMemoryStatusEx` P/Invoke.
- Grafana dashboard JSON not yet created (compose file includes Grafana service but no provisioned dashboards).
- No authentication on any endpoint (intentional for POC).
