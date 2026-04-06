# Monitoring API

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
- PostgreSQL stats are queried via EF Core keyless entities mapped to `pg_stat_activity`, `pg_stat_statements`, and `pg_stat_user_tables`

---

## Stack

| Concern | Choice |
|---|---|
| Framework | ASP.NET Core Minimal API (.NET 10) |
| ORM | Entity Framework Core 9 (`Npgsql.EntityFrameworkCore.PostgreSQL`) |
| Column naming | `EFCore.NamingConventions` — snake_case |
| Prometheus metrics | `prometheus-net.AspNetCore` + `prometheus-net.DotNetRuntime` |
| OpenAPI spec | `Microsoft.AspNetCore.OpenApi` (built-in .NET 10) |
| Swagger UI | `Swashbuckle.AspNetCore.SwaggerUI` at `/swagger` |
| Error responses | RFC 7807 Problem Details (`AddProblemDetails`) |
| Containerisation | Docker + Docker Compose |

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

## Option 4 — AWS CloudFormation (EC2)

Deploys the full stack on a single EC2 instance using Docker Compose. The CloudFormation template lives in `aws/cloudformation.yml`.

### Prerequisites

- An AWS account with an existing VPC, public subnet, and EC2 key pair
- AWS CLI configured (`aws configure`)

### Deploy

```bash
aws cloudformation create-stack \
  --stack-name monitoring-api \
  --template-body file://aws/cloudformation.yml \
  --capabilities CAPABILITY_NAMED_IAM \
  --parameters \
    ParameterKey=KeyPairName,ParameterValue=<your-key-pair> \
    ParameterKey=VpcId,ParameterValue=<vpc-id> \
    ParameterKey=SubnetId,ParameterValue=<subnet-id> \
    ParameterKey=GitRepoUrl,ParameterValue=https://github.com/your-org/NAPA_Interview.git \
    ParameterKey=GitBranch,ParameterValue=main
```

### What it creates

| Resource | Purpose |
|---|---|
| EC2 instance (Amazon Linux 2023) | Runs Docker Compose with the full stack |
| Security group | Opens ports 8080 (API), 3000 (Grafana), 9090 (Prometheus), 22 (SSH) |
| IAM role + instance profile | Enables SSM Session Manager as an SSH alternative |
| 30 GB gp3 EBS volume | Storage for Docker images, PostgreSQL data, Prometheus TSDB |

### After deployment

Once the stack reaches `CREATE_COMPLETE`, get the URLs from the stack outputs:

```bash
aws cloudformation describe-stacks --stack-name monitoring-api \
  --query "Stacks[0].Outputs" --output table
```

| Output | Example |
|---|---|
| SwaggerUrl | `http://<public-ip>:8080/swagger` |
| ApiOverviewUrl | `http://<public-ip>:8080/api/monitoring/overview` |
| GrafanaUrl | `http://<public-ip>:3000` (admin / admin) |
| PrometheusUrl | `http://<public-ip>:9090` |
| SshCommand | `ssh -i <key>.pem ec2-user@<public-ip>` |

> **Note:** The UserData script installs Docker, clones the repo, and runs `docker compose up`. Allow 2-3 minutes after stack creation for the services to become available. You can check progress with `ssh` and `tail -f /var/log/user-data.log`.

### Tear down

```bash
aws cloudformation delete-stack --stack-name monitoring-api
```

---

## Database-only Docker (for local .NET development)

If you want to run the API locally (Visual Studio or CLI) but still use Docker for PostgreSQL:

```bash
docker compose -f docker-compose.db-only.yml up -d
```

Then run the API via Visual Studio or the .NET CLI as described above.
