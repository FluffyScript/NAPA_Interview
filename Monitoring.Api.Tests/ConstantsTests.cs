using Monitoring.Api;

namespace Monitoring.Api.Tests;

/// <summary>
/// Guards against accidental constant changes that would break Prometheus dashboards,
/// routes, or configuration keys. If a constant changes, a test failure here forces
/// the developer to verify all downstream consumers (Prometheus alerts, Grafana
/// dashboards, docker-compose env vars, etc.) are updated too.
/// </summary>
public sealed class ConstantsTests
{
    // ── Metric names — changing these breaks Prometheus queries / Grafana dashboards ──

    [Theory]
    [InlineData("postgres_long_running_queries")]
    [InlineData("postgres_blocked_sessions")]
    [InlineData("postgres_active_sessions")]
    [InlineData("postgres_slow_query_count")]
    [InlineData("postgres_dead_tuples")]
    [InlineData("monitoring_collections_total")]
    [InlineData("monitoring_collection_duration_seconds")]
    [InlineData("dbhost_cpu_usage_percent")]
    [InlineData("dbhost_memory_total_bytes")]
    [InlineData("dbhost_memory_available_bytes")]
    public void MetricNames_AreNotAccidentallyChanged(string expectedName)
    {
        var allNames = new[]
        {
            Constants.Metrics.Names.LongRunningQueries,
            Constants.Metrics.Names.BlockedSessions,
            Constants.Metrics.Names.ActiveSessions,
            Constants.Metrics.Names.SlowQueryCount,
            Constants.Metrics.Names.DeadTuples,
            Constants.Metrics.Names.CollectionsTotal,
            Constants.Metrics.Names.CollectionDuration,
            Constants.Metrics.Names.DbHostCpuPercent,
            Constants.Metrics.Names.DbHostMemoryTotalBytes,
            Constants.Metrics.Names.DbHostMemoryAvailableBytes
        };

        Assert.Contains(expectedName, allNames);
    }

    [Fact]
    public void MetricLabels_Schema_IsCorrect()
    {
        Assert.Equal("schema", Constants.Metrics.Labels.Schema);
    }

    [Fact]
    public void MetricLabels_Table_IsCorrect()
    {
        Assert.Equal("table", Constants.Metrics.Labels.Table);
    }

    // ── Route paths — changing these breaks API consumers ────────────────────

    [Fact]
    public void RoutePaths_MonitoringGroup_IsApiMonitoring()
    {
        Assert.Equal("/api/monitoring", Constants.Routes.Paths.MonitoringGroup);
    }

    [Theory]
    [InlineData("/overview")]
    [InlineData("/long-running")]
    [InlineData("/blocked")]
    [InlineData("/dead-tuples")]
    [InlineData("/system")]
    [InlineData("/metrics")]
    public void RoutePaths_ContainsExpectedPaths(string expectedPath)
    {
        var allPaths = new[]
        {
            Constants.Routes.Paths.Overview,
            Constants.Routes.Paths.LongRunning,
            Constants.Routes.Paths.Blocked,
            Constants.Routes.Paths.DeadTuples,
            Constants.Routes.Paths.System,
            Constants.Routes.Paths.Metrics
        };

        Assert.Contains(expectedPath, allPaths);
    }

    // ── Configuration keys — changing these breaks appsettings.json binding ──

    [Fact]
    public void ConfigKeys_PostgresConnectionString_IsCorrect()
    {
        Assert.Equal("Postgres", Constants.Config.PostgresConnectionString);
    }

    [Fact]
    public void ConfigKeys_CollectionInterval_IsCorrect()
    {
        Assert.Equal("Monitoring:CollectionIntervalSeconds", Constants.Config.CollectionIntervalSeconds);
    }

    [Fact]
    public void ConfigKeys_NodeExporterUrl_IsCorrect()
    {
        Assert.Equal("Monitoring:NodeExporterUrl", Constants.Config.NodeExporterUrl);
    }

    // ── Database constants ───────────────────────────────────────────────────

    [Fact]
    public void Db_UndefinedTableSqlState_Is42P01()
    {
        Assert.Equal("42P01", Constants.Db.UndefinedTableSqlState);
    }

    [Fact]
    public void Db_DefaultOrderStatus_IsPending()
    {
        Assert.Equal("pending", Constants.Db.DefaultOrderStatus);
    }

    // ── Descriptions are non-empty (catch accidental clearing) ───────────────

    [Fact]
    public void MetricDescriptions_AreNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(Constants.Metrics.Descriptions.LongRunningQueries));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Metrics.Descriptions.BlockedSessions));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Metrics.Descriptions.ActiveSessions));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Metrics.Descriptions.SlowQueryCount));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Metrics.Descriptions.DeadTuples));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Metrics.Descriptions.CollectionsTotal));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Metrics.Descriptions.CollectionDuration));
    }

    [Fact]
    public void RouteDescriptions_AreNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(Constants.Routes.Descriptions.Overview));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Routes.Descriptions.LongRunning));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Routes.Descriptions.Blocked));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Routes.Descriptions.DeadTuples));
        Assert.False(string.IsNullOrWhiteSpace(Constants.Routes.Descriptions.System));
    }

    // ── API constants ────────────────────────────────────────────────────────

    [Fact]
    public void Api_Title_IsMonitoringApi()
    {
        Assert.Equal("Monitoring API", Constants.Api.Title);
    }

    [Fact]
    public void Api_SwaggerRoutePrefix_IsSwagger()
    {
        Assert.Equal("swagger", Constants.Api.SwaggerRoutePrefix);
    }
}
