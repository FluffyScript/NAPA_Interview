A .NET 10 Minimal API that monitors a PostgreSQL database and exposes metrics for Prometheus to scrape. 
Built as a proof of concept for a NAPA interview.

Architecture
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
Separation of concerns:

/api/monitoring/... → structured JSON for the frontend
/metrics → Prometheus scrape endpoint only, never for the frontend
PostgreSQL stats are queried directly via pg_stat_activity, pg_stat_statements, pg_stat_user_tables
Stack
Concern	Choice
Framework	ASP.NET Core Minimal API (.NET 10)
Prometheus metrics	prometheus-net.AspNetCore + prometheus-net.DotNetRuntime
OpenAPI spec	Microsoft.AspNetCore.OpenApi (built-in .NET 10)
Swagger UI	Swashbuckle.AspNetCore.SwaggerUI at /swagger
PostgreSQL access	Npgsql + Dapper (raw SQL against pg_stat_* views)
Error responses	RFC 7807 Problem Details (AddProblemDetails)
Containerisation	Docker + Docker Compose

# Monitoring API

.NET 10 Minimal API that monitors a PostgreSQL database and exposes health metrics as JSON endpoints and a Prometheus scrape endpoint.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (local install or via Docker)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (only if running with Docker)

---

## Option 1 — Visual Studio

1. Open `Monitoring.sln` in Visual Studio.
2. Make sure `Monitoring.Api` is set as the startup project.
3. Update the connection string in `Monitoring.Api/appsettings.json` if needed:
   ```json
   "ConnectionStrings": {
     "Postgres": "Host=localhost;Port=5432;Database=NAPA_Interview;Username=interview_user;Password=yourpassword"
   }
   ```
4. Press **F5** (or Ctrl+F5 to run without the debugger).

The app will open at the URLs defined in `Properties/launchSettings.json`:

| URL | Purpose |
|---|---|
| `http://localhost:64965/swagger` | Swagger UI |
| `http://localhost:64965/metrics` | Prometheus scrape endpoint |
| `http://localhost:64965/api/monitoring/overview` | JSON health snapshot |

---

## Option 2 — .NET CLI

> **Note:** On this machine `dotnet` is not on PATH. Use the full path: `"C:\Program Files\dotnet\dotnet.exe"`.

```bash
# Restore and build
"C:\Program Files\dotnet\dotnet.exe" build Monitoring.Api/Monitoring.Api.csproj

# Run
"C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj

# Run tests
"C:\Program Files\dotnet\dotnet.exe" test Monitoring.Api.Tests/Monitoring.Api.Tests.csproj
```

Same URLs as the Visual Studio option above.

---

## Option 3 — Docker Compose (full stack)

This starts the API, PostgreSQL, Prometheus, and Grafana together. No local PostgreSQL install required.

```bash
docker compose up --build
```

To stop everything:

```bash
docker compose down
```

| Service | URL | Notes |
|---|---|---|
| Swagger UI | `http://localhost:8080/swagger` | Interactive API docs |
| API Overview | `http://localhost:8080/api/monitoring/overview` | JSON health snapshot |
| Long-Running | `http://localhost:8080/api/monitoring/long-running` | Queries > 30s |
| Blocked | `http://localhost:8080/api/monitoring/blocked` | Lock-blocked sessions |
| Dead Tuples | `http://localhost:8080/api/monitoring/dead-tuples` | Tables needing VACUUM |
| System Metrics | `http://localhost:8080/api/monitoring/system` | CPU and memory stats |
| Prometheus | `http://localhost:9090` | Query metrics directly |
| Grafana | `http://localhost:3000` | Login: **admin / admin** |
| Prometheus Scrape | `http://localhost:8080/metrics` | Raw metrics for Prometheus |
| PostgreSQL | `localhost:5432` | Auto-initialized via `postgres/init.sql` |

The API waits for PostgreSQL to be healthy before starting.

### Grafana dashboard

A pre-built **Monitoring API** dashboard is auto-provisioned when the stack starts. It includes:

- **Top row** — stat panels for long-running queries, blocked sessions, active sessions, slow queries, CPU %, and memory
- **PostgreSQL sessions** — time-series of active, long-running, blocked, and slow queries
- **Process CPU & memory** — dual-axis chart (CPU % left, memory bytes right)
- **Dead tuples per table** — labeled by `schema.table`
- **Collection cycle duration** — average and p95 of the background collector
- **HTTP request rate & latency** — per-endpoint rate and p50/p95 response time
- **System memory** — total, available, and used RAM
- **.NET runtime** — GC collection rate, threadpool threads, exception rate

Open Grafana at `http://localhost:3000`, log in with **admin / admin**, and the dashboard will be available immediately under **Dashboards**.

---

## Database-only Docker (for local .NET development)

If you want to run the API locally (Visual Studio or CLI) but still use Docker for PostgreSQL:

```bash
docker compose -f docker-compose.db-only.yml up -d
```

Then run the API via Visual Studio or the .NET CLI as described above.
"# NAPA_Interview" 
