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
        Constants.Metrics.Names.LongRunningQueries,
        Constants.Metrics.Descriptions.LongRunningQueries);

    private static readonly Gauge BlockedSessions = Metrics.CreateGauge(
        Constants.Metrics.Names.BlockedSessions,
        Constants.Metrics.Descriptions.BlockedSessions);

    private static readonly Gauge ActiveSessions = Metrics.CreateGauge(
        Constants.Metrics.Names.ActiveSessions,
        Constants.Metrics.Descriptions.ActiveSessions);

    private static readonly Gauge SlowQueryCount = Metrics.CreateGauge(
        Constants.Metrics.Names.SlowQueryCount,
        Constants.Metrics.Descriptions.SlowQueryCount);

    private static readonly Gauge DeadTuplesPerTable = Metrics.CreateGauge(
        Constants.Metrics.Names.DeadTuples,
        Constants.Metrics.Descriptions.DeadTuples,
        new GaugeConfiguration
        {
            LabelNames = [Constants.Metrics.Labels.Schema, Constants.Metrics.Labels.Table]
        });

    private static readonly Counter CollectionsTotal = Metrics.CreateCounter(
        Constants.Metrics.Names.CollectionsTotal,
        Constants.Metrics.Descriptions.CollectionsTotal);

    private static readonly Histogram CollectionDuration = Metrics.CreateHistogram(
        Constants.Metrics.Names.CollectionDuration,
        Constants.Metrics.Descriptions.CollectionDuration,
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
            configuration.GetValue(Constants.Config.CollectionIntervalSeconds, 15));
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

        var longRunningTask = repo.GetLongRunningQueryCountAsync();
        var blockedTask     = repo.GetBlockedSessionCountAsync();
        var activeTask      = repo.GetTotalActiveSessionsAsync();
        var slowQueryTask   = repo.GetSlowQueryCountAsync();
        var deadTuplesTask  = repo.GetDeadTuplesAsync(minDeadTuples: 0);

        await Task.WhenAll(longRunningTask, blockedTask, activeTask, slowQueryTask, deadTuplesTask);

        LongRunningQueries.Set(await longRunningTask);
        BlockedSessions.Set(await blockedTask);
        ActiveSessions.Set(await activeTask);
        SlowQueryCount.Set(await slowQueryTask);

        foreach (var t in await deadTuplesTask)
        {
            DeadTuplesPerTable
                .WithLabels(t.SchemaName, t.TableName)
                .Set(t.DeadTupleCount);
        }

        _logger.LogDebug(
            "Metrics collected — long_running={L}, blocked={B}, active={A}",
            LongRunningQueries.Value, BlockedSessions.Value, ActiveSessions.Value);
    }
}
