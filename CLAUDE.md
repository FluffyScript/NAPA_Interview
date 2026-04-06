# CLAUDE.md — Monitoring API

## Project purpose
.NET 10 Minimal API that monitors a PostgreSQL database and exposes metrics for Prometheus to scrape. POC for a NAPA interview.

## How to build & run

```bash
# Build
"C:\Program Files\dotnet\dotnet.exe" build Monitoring.Api/Monitoring.Api.csproj

# Run (requires a reachable Postgres — see connection string section)
"C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj
```

`dotnet` is **not on PATH** on this machine. Always use the full path: `C:\Program Files\dotnet\dotnet.exe`.

| URL | Purpose |
|---|---|
| `http://localhost:64965/swagger` | Swagger UI (port from launchSettings.json) |
| `http://localhost:64965/metrics` | Prometheus scrape endpoint |
| `http://localhost:64965/api/monitoring/overview` | JSON health snapshot |

---

## Stack

| Concern | Library |
|---|---|
| Framework | ASP.NET Core Minimal API (.NET 10) |
| ORM | Entity Framework Core 9 (`Npgsql.EntityFrameworkCore.PostgreSQL`) |
| Column naming | `EFCore.NamingConventions` — snake_case |
| Prometheus metrics | `prometheus-net.AspNetCore` + `prometheus-net.DotNetRuntime` |
| OpenAPI spec | `Microsoft.AspNetCore.OpenApi` (built-in .NET 10) |
| Swagger UI | `Swashbuckle.AspNetCore.SwaggerUI` at `/swagger` |
| Error responses | RFC 7807 Problem Details |

---

## Project layout

```
Monitoring.Api/
  Program.cs                               — DI wiring, middleware, top-level routes
  Monitoring.Api.csproj
  appsettings.json                         — connection string, collection interval
  Data/
    MonitoringDbContext.cs                 — DbContext; ALL SQL lives here via ToSqlQuery()
    Entities/
      Order.cs                             — tracked entity → orders table
      LongRunningQueryRow.cs               — keyless → pg_stat_activity
      BlockedSessionRow.cs                 — keyless → pg_stat_activity (blocking join)
      DeadTuplesRow.cs                     — keyless → pg_stat_user_tables
      OverviewRow.cs                       — keyless → aggregate summary query
      SlowQueryRow.cs                      — keyless → pg_stat_statements
  Models/MonitoringModels.cs               — API response DTOs (C# records)
  Repositories/PostgresRepository.cs       — pure LINQ over DbSets; no SQL strings
  Routes/MonitoringRoutes.cs               — endpoint definitions as extension method
  Services/PostgresMonitoringCollector.cs  — BackgroundService → prometheus-net gauges
  Dockerfile
prometheus/prometheus.yml
postgres/init.sql
grafana/
  provisioning/datasources/prometheus.yml — auto-configures Prometheus data source
  provisioning/dashboards/default.yml     — tells Grafana where to find JSON dashboards
  dashboards/monitoring-api.json          — pre-built Monitoring API dashboard
aws/cloudformation.yml                    — CloudFormation template for EC2 deployment
docker-compose.yml
docker-compose.db-only.yml
Monitoring.Api.Tests/                      — xUnit test project
```

---

## Code of conduct

These are the rules applied throughout this codebase. Follow them when adding or changing code.

### 1. Layering — nothing skips a layer

```
HTTP request
    ↓
Routes (MonitoringRoutes)        — maps HTTP to method calls; no logic
    ↓
Repository (PostgresRepository)  — pure LINQ; no SQL strings; maps entities → DTOs
    ↓
DbContext (MonitoringDbContext)   — SQL, schema config, connection lifetime
    ↓
PostgreSQL
```

The background collector (`PostgresMonitoringCollector`) sits outside the HTTP path but still goes through the repository — it does not touch the DbContext directly.

### 2. SQL belongs in the DbContext, not in repositories

All SQL is defined once in `MonitoringDbContext.OnModelCreating` using `ToSqlQuery()` on keyless entities. Repositories express every query as LINQ:

```csharp
// Wrong — SQL in the repository
var rows = await _context.LongRunningQueries
    .FromSql($"SELECT ... WHERE duration_seconds > {threshold}")
    .ToListAsync();

// Right — LINQ in the repository, SQL in the DbContext
var rows = await _context.LongRunningQueries
    .Where(q => q.DurationSeconds > threshold)
    .OrderByDescending(q => q.DurationSeconds)
    .Take(50)
    .ToListAsync();
```

### 3. Entity lifetimes

| Type | DI lifetime | Reason |
|---|---|---|
| `MonitoringDbContext` | Scoped | Standard EF Core lifetime; one context per HTTP request or collector cycle |
| `PostgresRepository` | Scoped | Depends on DbContext |
| `PostgresMonitoringCollector` | Singleton (BackgroundService) | Long-lived; creates its own `AsyncScope` per cycle to get a fresh scoped repo |

Never inject a scoped service into a singleton directly — always resolve through `IServiceProvider.CreateAsyncScope()`.

### 4. One concern per class

- **Entities** — shape only; no methods, no logic, no attributes beyond what EF needs
- **DbContext** — connection + schema mapping only; no business logic
- **Repository** — query translation only; no HTTP concerns, no Prometheus updates
- **Routes** — HTTP binding only; delegate immediately to the repository
- **Collector** — metric update loop only; delegates all DB queries to the repository

### 5. DTOs are records; entities are classes

API response types in `Models/` are immutable C# records. EF entities in `Data/Entities/` are mutable classes (EF Core requires settable properties). Never expose an entity type directly through an API endpoint.

### 6. Error handling — catch at the boundary, not in the middle

Exceptions propagate naturally through repository and route layers. The global handler in `Program.cs` is the only catch site for infrastructure errors:

```csharp
// Wrong — catching in the repository for non-extension-specific errors
try { ... } catch (NpgsqlException) { return null; }

// Right — only catch what you can meaningfully handle locally
catch (PostgresException ex) when (ex.SqlState == "42P01") // extension absent
```

Database unavailability → `NpgsqlException` → caught by `UseExceptionHandler()` → `503 Problem Details`. Never return `null` or an empty result to mask an error.

### 7. Async all the way

Every method that touches I/O is `async Task<T>`. Never use `.Result`, `.Wait()`, or block on an async method. Use `ConfigureAwait(false)` on `Task.Delay` in background services (already done in the collector).

### 8. Concurrent independent queries

When multiple independent DB reads are needed, fire them together:

```csharp
var taskA = repo.GetXAsync();
var taskB = repo.GetYAsync();
await Task.WhenAll(taskA, taskB);
```

Never `await` sequentially when the results are independent.

### 9. Configuration over magic numbers

Thresholds and intervals come from `appsettings.json` under the `Monitoring:` section. Avoid hardcoded constants in business logic. The 30-second long-running threshold is an exception — it is part of the query definition in the DbContext, which is the appropriate place for it.

### 10. Checking impact when changing the repository

`PostgresRepository` is consumed by two independent callers:
- `Routes/MonitoringRoutes.cs` — HTTP path
- `Services/PostgresMonitoringCollector.cs` — background path

**After any change to the repository, always verify both files compile and behave correctly.** A method rename won't fail silently here (the compiler catches it), but a behavioural change (e.g. different filtering, different return shape) can break the collector without a build error.

---

## Unit tests

```bash
# Run all tests
"C:\Program Files\dotnet\dotnet.exe" test Monitoring.Api.Tests/Monitoring.Api.Tests.csproj
```

Tests live in `Monitoring.Api.Tests/` and use **xUnit** + **EF Core InMemory** provider.

| Test class | What it covers |
|---|---|
| `Repositories/PostgresRepositoryTests` | All repository methods — filtering, ordering, Take(50) limits, DTO mapping, defaults |
| `Services/PostgresMonitoringCollectorTests` | DI wiring, scoped resolution, graceful cancellation, config binding |
| `Services/SystemMetricsServiceTests` | Snapshot lifecycle, metric formatting, graceful shutdown |
| `Models/MonitoringModelsTests` | DTO construction, value equality, nullable fields |
| `Data/Entities/EntityTests` | Entity defaults, property setters, nullability |
| `ConstantsTests` | Guards against accidental metric name / route path / config key changes |

### 11. Always update tests when changing code

**After any code change, run the test suite and update tests to match.** Specifically:

- **Repository changes** → update `PostgresRepositoryTests` (filtering, ordering, new methods)
- **New or renamed routes** → update `MonitoringRoutes` tests if present, verify `ConstantsTests`
- **DTO shape changes** → update `MonitoringModelsTests` and any repository tests that assert on DTO properties
- **New constants / metric names** → add corresponding assertions in `ConstantsTests`
- **Entity changes** → update `EntityTests` (defaults, new properties)
- **Collector / service changes** → update the corresponding service test class

Never skip tests. If a test fails after a code change, fix the test to match the new intended behaviour — do not delete it.

---

## Connection string

`appsettings.json` → `ConnectionStrings:Postgres`
Override via env var: `ConnectionStrings__Postgres`

```
Host=localhost;Port=5432;Database=NAPA_Interview;Username=interview_user;Password=...
```

See `startme.md` for local vs Docker switching instructions.

---

## Migrations

EF Core migrations apply to the `orders` table only. Keyless entities (`ToSqlQuery`) are excluded from migrations automatically.

```bash
# Add a migration
"C:\Program Files\dotnet\dotnet.exe" ef migrations add <Name> --project Monitoring.Api

# Apply to database
"C:\Program Files\dotnet\dotnet.exe" ef database update --project Monitoring.Api
```

---

## pg_stat_statements

`SlowQueryRow` maps to `pg_stat_statements`. The extension must be loaded via `shared_preload_libraries`. Both compose files pass `-c shared_preload_libraries=pg_stat_statements` to Postgres. If absent on a local install, `GetSlowQueryCountAsync` catches `PostgresException(42P01)` and returns 0.
