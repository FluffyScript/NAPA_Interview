# Start Me

Two ways to run the project. Pick the one that fits your situation.

---

## Mode A — Local API + Docker DB (recommended for development)

The API runs on your machine with `dotnet run`. Only PostgreSQL runs in Docker.  
`appsettings.json` already has the correct connection string pointing to `localhost:5432`, so **no changes needed**.

### Start the database

```bash
docker compose -f docker-compose.db-only.yml up -d
```

### Run the API

```bash
"C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj
```

### Stop the database

```bash
docker compose -f docker-compose.db-only.yml down
```

To also delete the stored data:

```bash
docker compose -f docker-compose.db-only.yml down -v
```

---

## Mode B — Full Docker stack (API + DB + Prometheus + Grafana)

Everything runs in containers. The API container receives its connection string via environment variable — `appsettings.json` is **not used**.

### Start everything

```bash
docker compose up --build
```

### Start without rebuilding the API image

```bash
docker compose up
```

### Stop everything (keep data volumes)

```bash
docker compose down
```

### Stop and delete all data

```bash
docker compose down -v
```

---

## URLs

| Service | Mode A | Mode B |
|---|---|---|
| API | `http://localhost:5000` | `http://localhost:8080` |
| Swagger UI | `http://localhost:5000/swagger` | `http://localhost:8080/swagger` |
| Prometheus scrape | `http://localhost:5000/metrics` | `http://localhost:8080/metrics` |
| Prometheus UI | — | `http://localhost:9090` |
| Grafana | — | `http://localhost:3000` (admin / admin) |
| PostgreSQL | `localhost:5432` | `localhost:5432` |

---

## Switching between modes

### From Mode A to Mode B

1. Stop the DB-only container:
   ```bash
   docker compose -f docker-compose.db-only.yml down
   ```
2. Start the full stack:
   ```bash
   docker compose up --build
   ```
   The full stack uses the same `postgres_data` volume, so your data is preserved.

### From Mode B to Mode A

1. Stop the full stack:
   ```bash
   docker compose down
   ```
2. Start just the database:
   ```bash
   docker compose -f docker-compose.db-only.yml up -d
   ```
3. Run the API locally:
   ```bash
   "C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj
   ```

> Both compose files use a volume named `postgres_data`. Docker will reuse the same volume whichever file started it, so your data carries over between modes automatically.

---

## Connection strings reference

### Mode A — appsettings.json (already configured, no changes needed)

```
Host=localhost;Port=5432;Database=NAPA_Interview;Username=interview_user;Password=ihnte$*(&)%Yhjn9y53
```

### Mode B — injected by docker-compose.yml via environment variable

```
Host=postgres;Port=5432;Database=NAPA_Interview;Username=interview_user;Password=ihnte$*(&)%Yhjn9y53
```

The only difference is `Host=localhost` (Mode A) vs `Host=postgres` (Mode B).  
`postgres` is the service name inside Docker's internal network.

### Override for Mode A without editing appsettings.json

Set the environment variable before running:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5432;Database=NAPA_Interview;Username=interview_user;Password=ihnte`$*(&)%Yhjn9y53"
"C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj
```

> Note the backtick before `$` in PowerShell to prevent variable interpolation.

---

## Password note

The database password (`ihnte$*(&)%Yhjn9y53`) contains a `$` character.

| Context | How to write it |
|---|---|
| `appsettings.json` | `ihnte$*(&)%Yhjn9y53` — literal, no escaping needed |
| `docker-compose.yml` | `ihnte$$*(&)%Yhjn9y53` — `$$` is Docker Compose's escape for a literal `$` |
| PowerShell env var | `` ihnte`$*(&)%Yhjn9y53 `` — backtick escapes `$` in PowerShell |
| cmd.exe env var | `ihnte$*(&)%Yhjn9y53` — no escaping needed in cmd |

---

## Troubleshooting

**Port 5432 already in use**
A local PostgreSQL service is running. Either stop it or change the host port mapping in the compose file (`"5433:5432"`) and update the connection string accordingly.

**API exits immediately in Mode B**
The healthcheck on the Postgres container prevents the API from starting until the DB is ready. If it still fails, check the logs:
```bash
docker compose logs postgres
docker compose logs monitoring-api
```

**pg_stat_statements errors**
Both compose files pass `-c shared_preload_libraries=pg_stat_statements` to the Postgres process. If you see errors about the extension, the volume may contain an old data directory initialised without that flag. Delete the volume and restart:
```bash
docker compose down -v
docker compose up -d
```
