using Monitoring.Api.Repositories;
using Prometheus;

namespace Monitoring.Api.Services;

/// <summary>
/// Background service that polls PostgreSQL system views on a fixed interval
/// and updates prometheus-net gauges. The /metrics endpoint exposes them for Prometheus to scrape.
/// </summary>
public sealed class PostgresMonitoringCollector : BackgroundService
{
    // --- Prometheus instruments (static so they survive DI scope changes) ---

    private static readonly Gauge LongRunningQueries = Metrics.CreateGauge(
        "postgres_long_running_queries",
        "Number of queries running longer than 30 s.");

    private static readonly Gauge BlockedSessions = Metrics.CreateGauge(
        "postgres_blocked_sessions",
        "Number of sessions currently blocked by a lock.");

    private static readonly Gauge ActiveSessions = Metrics.CreateGauge(
        "postgres_active_sessions",
        "Total number of active PostgreSQL sessions.");

    private static readonly Gauge SlowQueryCount = Metrics.CreateGauge(
        "postgres_slow_query_count",
        "Queries with mean execution time > 1 s (requires pg_stat_statements extension).");

    // Labeled gauge: one time-series per {schema, table}
    private static readonly Gauge DeadTuplesPerTable = Metrics.CreateGauge(
        "postgres_dead_tuples",
        "Dead tuple count per user table.",
        new GaugeConfiguration { LabelNames = ["schema", "table"] });

    private static readonly Counter CollectionsTotal = Metrics.CreateCounter(
        "monitoring_collections_total",
        "Total number of completed Postgres metric collection cycles.");

    private static readonly Histogram CollectionDuration = Metrics.CreateHistogram(
        "monitoring_collection_duration_seconds",
        "Time taken to complete one Postgres metric collection cycle.",
        new HistogramConfiguration
        {
            Buckets = Histogram.LinearBuckets(start: 0.01, width: 0.05, count: 10)
        });

    // ---

    private readonly ILogger<PostgresMonitoringCollector> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _interval;

    public PostgresMonitoringCollector(
        ILogger<PostgresMonitoringCollector> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _interval = TimeSpan.FromSeconds(
            configuration.GetValue("Monitoring:CollectionIntervalSeconds", 15));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PostgresMonitoringCollector started. Collection interval: {Interval}s",
            _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            using (CollectionDuration.NewTimer())
            {
                try
                {
                    await CollectAsync(stoppingToken);
                    CollectionsTotal.Inc();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Postgres metric collection failed.");
                }
            }

            await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("PostgresMonitoringCollector stopped.");
    }

    private async Task CollectAsync(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<PostgresRepository>();

        // Fire all independent queries concurrently
        var longRunningTask = repo.GetLongRunningQueryCountAsync();
        var blockedTask     = repo.GetBlockedSessionCountAsync();
        var activeTask      = repo.GetTotalActiveSessionsAsync();
        var slowQueryTask   = repo.GetSlowQueryCountAsync();
        var deadTuplesTask  = repo.GetDeadTuplesAsync(minDeadTuples: 0); // all tables for labels

        await Task.WhenAll(longRunningTask, blockedTask, activeTask, slowQueryTask, deadTuplesTask);

        LongRunningQueries.Set(await longRunningTask);
        BlockedSessions.Set(await blockedTask);
        ActiveSessions.Set(await activeTask);
        SlowQueryCount.Set(await slowQueryTask);

        // Update labeled dead-tuple gauge per table
        foreach (var t in await deadTuplesTask)
        {
            DeadTuplesPerTable.WithLabels(t.SchemaName, t.TableName).Set(t.DeadTupleCount);
        }

        _logger.LogDebug(
            "Metrics collected — long_running={L}, blocked={B}, active={A}",
            LongRunningQueries.Value, BlockedSessions.Value, ActiveSessions.Value);
    }
}
