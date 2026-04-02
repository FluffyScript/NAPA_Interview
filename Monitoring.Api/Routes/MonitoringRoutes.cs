using Monitoring.Api.Models;
using Monitoring.Api.Repositories;

namespace Monitoring.Api.Routes;

public static class MonitoringRoutes
{
    public static IEndpointRouteBuilder MapMonitoringRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitoring")
            .WithTags("Monitoring")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/overview", async (PostgresRepository repo) =>
        {
            var data = await repo.GetOverviewAsync();
            return Results.Ok(data);
        })
        .WithName("GetOverview")
        .WithSummary("Database overview")
        .WithDescription("Returns a snapshot of active sessions, blocked sessions, long-running queries, and the maximum query duration.")
        .Produces<OverviewDto>();

        group.MapGet("/long-running", async (
            PostgresRepository repo,
            int thresholdSeconds = 30) =>
        {
            var queries = await repo.GetLongRunningQueriesAsync(thresholdSeconds);
            return Results.Ok(queries);
        })
        .WithName("GetLongRunningQueries")
        .WithSummary("Long-running queries")
        .WithDescription("Lists queries that have been running longer than `thresholdSeconds` (default 30 s), ordered by duration descending.")
        .Produces<IEnumerable<LongRunningQueryDto>>();

        group.MapGet("/blocked", async (PostgresRepository repo) =>
        {
            var sessions = await repo.GetBlockedSessionsAsync();
            return Results.Ok(sessions);
        })
        .WithName("GetBlockedSessions")
        .WithSummary("Blocked sessions")
        .WithDescription("Lists sessions currently blocked by a lock, paired with the blocking session.")
        .Produces<IEnumerable<BlockedSessionDto>>();

        group.MapGet("/dead-tuples", async (
            PostgresRepository repo,
            int minDeadTuples = 100) =>
        {
            var tables = await repo.GetDeadTuplesAsync(minDeadTuples);
            return Results.Ok(tables);
        })
        .WithName("GetDeadTuples")
        .WithSummary("Dead tuples per table")
        .WithDescription("Lists user tables with at least `minDeadTuples` dead tuples (default 100), ordered by dead tuple count descending.")
        .Produces<IEnumerable<DeadTuplesDto>>();

        return app;
    }
}
