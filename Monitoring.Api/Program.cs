using Monitoring.Api.Repositories;
using Monitoring.Api.Services;
using Npgsql;
using Prometheus;
using Prometheus.DotNetRuntime;

var builder = WebApplication.CreateBuilder(args);

// --- OpenAPI ---
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info.Title       = "Monitoring API";
        doc.Info.Version     = "v1";
        doc.Info.Description =
            "PostgreSQL monitoring API — exposes database health metrics as JSON endpoints " +
            "and a Prometheus-compatible /metrics scrape endpoint.";
        return Task.CompletedTask;
    });
});

// --- Problem Details (RFC 7807) — maps DB errors to 503 ---
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        if (ctx.Exception is NpgsqlException)
        {
            ctx.ProblemDetails.Status = StatusCodes.Status503ServiceUnavailable;
            ctx.ProblemDetails.Title  = "Database Unavailable";
            ctx.ProblemDetails.Detail = "Unable to reach the database. Please try again later.";
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    };
});

// --- Repositories & background services ---
builder.Services.AddSingleton<PostgresRepository>();
builder.Services.AddHostedService<PostgresMonitoringCollector>();

var app = builder.Build();

// --- .NET runtime metrics (GC, threadpool, contention, exceptions) ---
DotNetRuntimeStatsBuilder
    .Customize()
    .WithGcStats()
    .WithThreadPoolStats()
    .WithContentionStats()
    .WithExceptionStats()
    .StartCollecting();

// --- Global exception handler (uses Problem Details configured above) ---
app.UseExceptionHandler();

// --- HTTP metrics middleware (request count, duration, in-flight) ---
app.UseHttpMetrics();

// --- OpenAPI / Swagger UI ---
app.MapOpenApi();  // serves /openapi/v1.json

app.UseSwaggerUI(opts =>
{
    opts.SwaggerEndpoint("/openapi/v1.json", "Monitoring API v1");
    opts.RoutePrefix = "swagger";  // → http://localhost:8080/swagger
});

// --- Routes ---

app.MapGet("/", () => "Monitoring API is running")
   .ExcludeFromDescription();

// Overview: top-level snapshot used by dashboard cards
app.MapGet("/api/monitoring/overview", async (PostgresRepository repo) =>
{
    var data = await repo.GetOverviewAsync();
    return Results.Ok(data);
})
.WithName("GetOverview")
.WithSummary("Database overview")
.WithDescription("Returns a snapshot of active sessions, blocked sessions, long-running queries, and the maximum query duration.")
.WithTags("Monitoring")
.Produces<Monitoring.Api.Models.OverviewDto>()
.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Long-running queries
app.MapGet("/api/monitoring/long-running", async (
    PostgresRepository repo,
    int thresholdSeconds = 30) =>
{
    var queries = await repo.GetLongRunningQueriesAsync(thresholdSeconds);
    return Results.Ok(queries);
})
.WithName("GetLongRunningQueries")
.WithSummary("Long-running queries")
.WithDescription("Lists queries that have been running longer than `thresholdSeconds` (default 30 s), ordered by duration descending.")
.WithTags("Monitoring")
.Produces<IEnumerable<Monitoring.Api.Models.LongRunningQueryDto>>()
.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Blocked sessions
app.MapGet("/api/monitoring/blocked", async (PostgresRepository repo) =>
{
    var sessions = await repo.GetBlockedSessionsAsync();
    return Results.Ok(sessions);
})
.WithName("GetBlockedSessions")
.WithSummary("Blocked sessions")
.WithDescription("Lists sessions currently blocked by a lock, paired with the blocking session.")
.WithTags("Monitoring")
.Produces<IEnumerable<Monitoring.Api.Models.BlockedSessionDto>>()
.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Dead tuples per table
app.MapGet("/api/monitoring/dead-tuples", async (
    PostgresRepository repo,
    int minDeadTuples = 100) =>
{
    var tables = await repo.GetDeadTuplesAsync(minDeadTuples);
    return Results.Ok(tables);
})
.WithName("GetDeadTuples")
.WithSummary("Dead tuples per table")
.WithDescription("Lists user tables with at least `minDeadTuples` dead tuples (default 100), ordered by dead tuple count descending.")
.WithTags("Monitoring")
.Produces<IEnumerable<Monitoring.Api.Models.DeadTuplesDto>>()
.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Prometheus scrape endpoint — for Prometheus only, not the frontend
app.MapMetrics("/metrics");

app.Run();
