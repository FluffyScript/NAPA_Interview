namespace Monitoring.Api;

/// <summary>
/// Single source of truth for every named string used across the solution.
/// Nested classes group constants by concern so callsites are self-documenting:
///   Constants.Metrics.Names.BlockedSessions
///   Constants.Routes.Paths.LongRunning
///   Constants.Config.CollectionIntervalSeconds
/// </summary>
public static class Constants
{
    // ── Prometheus metrics ────────────────────────────────────────────────────

    public static class Metrics
    {
        public static class Names
        {
            public const string LongRunningQueries = "postgres_long_running_queries";
            public const string BlockedSessions     = "postgres_blocked_sessions";
            public const string ActiveSessions      = "postgres_active_sessions";
            public const string SlowQueryCount      = "postgres_slow_query_count";
            public const string DeadTuples          = "postgres_dead_tuples";
            public const string CollectionsTotal    = "monitoring_collections_total";
            public const string CollectionDuration  = "monitoring_collection_duration_seconds";
        }

        public static class Descriptions
        {
            public const string LongRunningQueries = "Number of queries running longer than 30 s.";
            public const string BlockedSessions     = "Number of sessions currently blocked by a lock.";
            public const string ActiveSessions      = "Total number of active PostgreSQL sessions.";
            public const string SlowQueryCount      = "Queries with mean execution time > 1 s (requires pg_stat_statements extension).";
            public const string DeadTuples          = "Dead tuple count per user table.";
            public const string CollectionsTotal    = "Total number of completed Postgres metric collection cycles.";
            public const string CollectionDuration  = "Time taken to complete one Postgres metric collection cycle.";
        }

        public static class Labels
        {
            public const string Schema = "schema";
            public const string Table  = "table";
        }
    }

    // ── HTTP routes ───────────────────────────────────────────────────────────

    public static class Routes
    {
        public static class Paths
        {
            public const string Root            = "/";
            public const string MonitoringGroup = "/api/monitoring";
            public const string Overview        = "/overview";
            public const string LongRunning     = "/long-running";
            public const string Blocked         = "/blocked";
            public const string DeadTuples      = "/dead-tuples";
            public const string Metrics         = "/metrics";
        }

        public static class Names
        {
            public const string GetOverview          = "GetOverview";
            public const string GetLongRunningQueries = "GetLongRunningQueries";
            public const string GetBlockedSessions   = "GetBlockedSessions";
            public const string GetDeadTuples        = "GetDeadTuples";
        }

        public static class Tags
        {
            public const string Monitoring = "Monitoring";
        }

        public static class Summaries
        {
            public const string Overview    = "Database overview";
            public const string LongRunning = "Long-running queries";
            public const string Blocked     = "Blocked sessions";
            public const string DeadTuples  = "Dead tuples per table";
        }

        public static class Descriptions
        {
            public const string Overview =
                "Returns a snapshot of active sessions, blocked sessions, long-running queries, and the maximum query duration.";

            public const string LongRunning =
                "Lists queries that have been running longer than `thresholdSeconds` (default 30 s), ordered by duration descending.";

            public const string Blocked =
                "Lists sessions currently blocked by a lock, paired with the blocking session.";

            public const string DeadTuples =
                "Lists user tables with at least `minDeadTuples` dead tuples (default 100), ordered by dead tuple count descending.";
        }
    }

    // ── Configuration keys ────────────────────────────────────────────────────

    public static class Config
    {
        public const string PostgresConnectionString  = "Postgres";
        public const string CollectionIntervalSeconds = "Monitoring:CollectionIntervalSeconds";
    }

    // ── OpenAPI / Swagger ─────────────────────────────────────────────────────

    public static class Api
    {
        public const string Title              = "Monitoring API";
        public const string Version            = "v1";
        public const string Description        =
            "PostgreSQL monitoring API — exposes database health metrics as JSON endpoints " +
            "and a Prometheus-compatible /metrics scrape endpoint.";
        public const string OpenApiJsonEndpoint = "/openapi/v1.json";
        public const string SwaggerEndpointTitle = "Monitoring API v1";
        public const string SwaggerRoutePrefix  = "swagger";
    }

    // ── Error responses ───────────────────────────────────────────────────────

    public static class Errors
    {
        public const string DatabaseUnavailableTitle  = "Database Unavailable";
        public const string DatabaseUnavailableDetail = "Unable to reach the database. Please try again later.";
    }

    // ── Database ──────────────────────────────────────────────────────────────

    public static class Db
    {
        /// <summary>PostgreSQL error code for "relation does not exist" (undefined table/view).</summary>
        public const string UndefinedTableSqlState = "42P01";

        public const string DefaultOrderStatus  = "pending";
        public const string TimestampDefaultSql = "now()";
    }
}
