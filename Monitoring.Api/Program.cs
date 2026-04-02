using Microsoft.EntityFrameworkCore;
using Monitoring.Api;
using Monitoring.Api.Data;
using Monitoring.Api.Repositories;
using Monitoring.Api.Routes;
using Monitoring.Api.Services;
using Npgsql;
using Prometheus;
using Prometheus.DotNetRuntime;
using static Monitoring.Api.Constants;

var builder = WebApplication.CreateBuilder(args);

// --- OpenAPI ---
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info.Title       = Api.Title;
        doc.Info.Version     = Api.Version;
        doc.Info.Description = Api.Description;
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
            ctx.ProblemDetails.Title  = Errors.DatabaseUnavailableTitle;
            ctx.ProblemDetails.Detail = Errors.DatabaseUnavailableDetail;
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    };
});

// --- Entity Framework Core ---
builder.Services.AddDbContext<MonitoringDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString(Config.PostgresConnectionString))
           .UseSnakeCaseNamingConvention());

// --- Repositories & background services ---
builder.Services.AddScoped<PostgresRepository>();
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
    opts.SwaggerEndpoint(Api.OpenApiJsonEndpoint, Api.SwaggerEndpointTitle);
    opts.RoutePrefix = Api.SwaggerRoutePrefix;
});

// --- Routes ---
app.MapGet(Routes.Paths.Root, () => "Monitoring API is running").ExcludeFromDescription();
app.MapMonitoringRoutes();
app.MapMetrics(Routes.Paths.Metrics);

app.Run();
