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
       │                    ┌────────┤
┌──────▼──────┐             │        ▼
│   Grafana   │    ┌────────▼─────┐ ┌──────────────┐
│  :3000      │    │ node_exporter│ │  PostgreSQL  │
└─────────────┘    │  :9100       │ │  :5432       │
                   └──────────────┘ └──────────────┘
                    (shares PID/net namespace)
```

**Separation of concerns:**
- `/api/monitoring/...` → structured JSON for the frontend
- `/api/monitoring/system` → database host CPU/memory from node_exporter
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
| DB host metrics | `node_exporter` — CPU/memory from the PostgreSQL host |
| Containerisation | Docker + Docker Compose |

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — required for all run options
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — only needed for building/testing locally

> **Why Docker?** The `/api/monitoring/system` endpoint fetches CPU and memory metrics from a `node_exporter` instance running alongside PostgreSQL. Prometheus and Grafana also run as containers. Running outside Docker means no system metrics, no dashboards, and no metric scraping — so **Docker Compose is the recommended (and only complete) way to run the solution.**

---

## Option 1 — Docker Compose (full stack, recommended)

This starts the API, PostgreSQL, node_exporter, Prometheus, and Grafana together. No local installs required beyond Docker.

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
| System Metrics | `http://localhost:8080/api/monitoring/system` | DB host CPU and memory (via node_exporter) |
| Prometheus | `http://localhost:9090` | Query metrics directly |
| Grafana | `http://localhost:3000` | Login: **admin / admin** |
| Prometheus Scrape | `http://localhost:8080/metrics` | Raw metrics for Prometheus |
| PostgreSQL | `localhost:5432` | Auto-initialized via `postgres/init.sql` |

The API waits for PostgreSQL to be healthy before starting.

### Grafana dashboard

A pre-built **Monitoring API** dashboard is auto-provisioned when the stack starts. It includes:

- **Top row** — stat panels for long-running queries, blocked sessions, active sessions, slow queries, DB host CPU %, and DB host memory used
- **PostgreSQL sessions** — time-series of active, long-running, blocked, and slow queries
- **DB host CPU & memory** — dual-axis chart (CPU % left, memory used right) sourced from node_exporter
- **Dead tuples per table** — labeled by `schema.table`
- **Collection cycle duration** — average and p95 of the background collector
- **HTTP request rate & latency** — per-endpoint rate and p50/p95 response time
- **DB host memory** — total, available, and used RAM on the database server
- **.NET runtime** — GC collection rate, threadpool threads, exception rate

Open Grafana at `http://localhost:3000`, log in with **admin / admin**, and the dashboard will be available immediately under **Dashboards**.

---

## Option 2 — AWS CloudFormation (EC2)

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

## Local development (build & test only)

The full stack requires Docker Compose (Option 1). However, you can still build and run tests locally without Docker:

> **Note:** On this machine `dotnet` is not on PATH. Use the full path: `"C:\Program Files\dotnet\dotnet.exe"`.

```bash
# Build
"C:\Program Files\dotnet\dotnet.exe" build Monitoring.Api/Monitoring.Api.csproj

# Run tests (no database or Docker required)
"C:\Program Files\dotnet\dotnet.exe" test Monitoring.Api.Tests/Monitoring.Api.Tests.csproj
```

If you need to run the API locally against a real database (e.g. for debugging), use the database-only compose file which starts PostgreSQL and node_exporter:

```bash
docker compose -f docker-compose.db-only.yml up -d
```

Then run the API via Visual Studio (**F5**) or the CLI:

```bash
"C:\Program Files\dotnet\dotnet.exe" run --project Monitoring.Api/Monitoring.Api.csproj
```

> **Limitations when running locally:** Prometheus and Grafana are not available — there is no metric scraping or dashboards. The `/api/monitoring/system` endpoint will still work because `docker-compose.db-only.yml` includes node_exporter alongside PostgreSQL (exposed on `localhost:9100`). All other database monitoring endpoints work normally.
