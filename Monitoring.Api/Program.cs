using Monitoring.Api.Repositories;
using Monitoring.Api.Routes;
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

// --- Middleware ---
app.UseExceptionHandler();
app.UseHttpMetrics();

// --- OpenAPI / Swagger UI ---
app.MapOpenApi();
app.UseSwaggerUI(opts =>
{
    opts.SwaggerEndpoint("/openapi/v1.json", "Monitoring API v1");
    opts.RoutePrefix = "swagger";
});

// --- Routes ---
app.MapGet("/", () => "Monitoring API is running").ExcludeFromDescription();
app.MapMonitoringRoutes();
app.MapMetrics("/metrics");

app.Run();
