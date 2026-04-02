using Monitoring.Api.Models;
using Monitoring.Api.Repositories;

namespace Monitoring.Api.Routes;

public static class MonitoringRoutes
{
    public static IEndpointRouteBuilder MapMonitoringRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Constants.Routes.Paths.MonitoringGroup)
            .WithTags(Constants.Routes.Tags.Monitoring)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(Constants.Routes.Paths.Overview, async (PostgresRepository repo) =>
        {
            var data = await repo.GetOverviewAsync();
            return Results.Ok(data);
        })
        .WithName(Constants.Routes.Names.GetOverview)
        .WithSummary(Constants.Routes.Summaries.Overview)
        .WithDescription(Constants.Routes.Descriptions.Overview)
        .Produces<OverviewDto>();

        group.MapGet(Constants.Routes.Paths.LongRunning, async (
            PostgresRepository repo,
            int thresholdSeconds = 30) =>
        {
            var queries = await repo.GetLongRunningQueriesAsync(thresholdSeconds);
            return Results.Ok(queries);
        })
        .WithName(Constants.Routes.Names.GetLongRunningQueries)
        .WithSummary(Constants.Routes.Summaries.LongRunning)
        .WithDescription(Constants.Routes.Descriptions.LongRunning)
        .Produces<IEnumerable<LongRunningQueryDto>>();

        group.MapGet(Constants.Routes.Paths.Blocked, async (PostgresRepository repo) =>
        {
            var sessions = await repo.GetBlockedSessionsAsync();
            return Results.Ok(sessions);
        })
        .WithName(Constants.Routes.Names.GetBlockedSessions)
        .WithSummary(Constants.Routes.Summaries.Blocked)
        .WithDescription(Constants.Routes.Descriptions.Blocked)
        .Produces<IEnumerable<BlockedSessionDto>>();

        group.MapGet(Constants.Routes.Paths.DeadTuples, async (
            PostgresRepository repo,
            int minDeadTuples = 100) =>
        {
            var tables = await repo.GetDeadTuplesAsync(minDeadTuples);
            return Results.Ok(tables);
        })
        .WithName(Constants.Routes.Names.GetDeadTuples)
        .WithSummary(Constants.Routes.Summaries.DeadTuples)
        .WithDescription(Constants.Routes.Descriptions.DeadTuples)
        .Produces<IEnumerable<DeadTuplesDto>>();

        return app;
    }
}
