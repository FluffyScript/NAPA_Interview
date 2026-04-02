using Dapper;
using Monitoring.Api.Models;
using Npgsql;

namespace Monitoring.Api.Repositories;

public sealed class PostgresRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresRepository> _logger;

    public PostgresRepository(IConfiguration configuration, ILogger<PostgresRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        _logger = logger;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<OverviewDto> GetOverviewAsync()
    {
        const string sql = """
            SELECT
                (
                    SELECT COUNT(*)
                    FROM pg_stat_activity
                    WHERE state = 'active'
                      AND now() - query_start > interval '30 seconds'
                      AND query NOT ILIKE '%pg_stat_activity%'
                ) AS long_running_count,
                (
                    SELECT COUNT(*)
                    FROM pg_stat_activity
                    WHERE wait_event_type = 'Lock'
                ) AS blocked_count,
                (
                    SELECT COUNT(*)
                    FROM pg_stat_activity
                    WHERE state = 'active'
                ) AS active_count,
                COALESCE(
                    (
                        SELECT EXTRACT(EPOCH FROM MAX(now() - query_start))
                        FROM pg_stat_activity
                        WHERE state = 'active'
                          AND query NOT ILIKE '%pg_stat_activity%'
                    ),
                    0
                ) AS max_duration_seconds
            """;

        await using var conn = CreateConnection();
        var result = await conn.QuerySingleAsync(sql);

        return new OverviewDto(
            LongRunningQueryCount: (int)result.long_running_count,
            BlockedSessionCount: (int)result.blocked_count,
            TotalActiveSessions: (int)result.active_count,
            MaxQueryDurationSeconds: (double)result.max_duration_seconds,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    public async Task<IEnumerable<LongRunningQueryDto>> GetLongRunningQueriesAsync(int thresholdSeconds = 30)
    {
        const string sql = """
            SELECT
                pid,
                state,
                query,
                EXTRACT(EPOCH FROM (now() - query_start)) AS duration_seconds,
                COALESCE(application_name, '') AS application_name
            FROM pg_stat_activity
            WHERE state = 'active'
              AND now() - query_start > make_interval(secs => @threshold)
              AND query NOT ILIKE '%pg_stat_activity%'
            ORDER BY duration_seconds DESC
            LIMIT 50
            """;

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync(sql, new { threshold = thresholdSeconds });

        return rows.Select(r => new LongRunningQueryDto(
            Pid: (int)r.pid,
            State: (string)r.state,
            Query: (string)r.query,
            DurationSeconds: (double)r.duration_seconds,
            ApplicationName: (string)r.application_name
        ));
    }

    public async Task<IEnumerable<BlockedSessionDto>> GetBlockedSessionsAsync()
    {
        const string sql = """
            SELECT
                blocked.pid          AS blocked_pid,
                blocked.query        AS blocked_query,
                blocking.pid         AS blocking_pid,
                blocking.query       AS blocking_query
            FROM pg_stat_activity AS blocked
            JOIN pg_stat_activity AS blocking
                ON blocking.pid = ANY(pg_blocking_pids(blocked.pid))
            WHERE cardinality(pg_blocking_pids(blocked.pid)) > 0
            """;

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync(sql);

        return rows.Select(r => new BlockedSessionDto(
            BlockedPid: (int)r.blocked_pid,
            BlockedQuery: (string)r.blocked_query,
            BlockingPid: (int)r.blocking_pid,
            BlockingQuery: (string)r.blocking_query
        ));
    }

    public async Task<IEnumerable<DeadTuplesDto>> GetDeadTuplesAsync(int minDeadTuples = 100)
    {
        const string sql = """
            SELECT
                schemaname  AS schema_name,
                relname     AS table_name,
                n_dead_tup  AS dead_tuple_count,
                n_live_tup  AS live_tuple_count
            FROM pg_stat_user_tables
            WHERE n_dead_tup >= @minDeadTuples
            ORDER BY n_dead_tup DESC
            LIMIT 50
            """;

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync(sql, new { minDeadTuples });

        return rows.Select(r => new DeadTuplesDto(
            SchemaName: (string)r.schema_name,
            TableName: (string)r.table_name,
            DeadTupleCount: (long)r.dead_tuple_count,
            LiveTupleCount: (long)r.live_tuple_count
        ));
    }

    // --- Methods used by the background collector ---

    public async Task<int> GetLongRunningQueryCountAsync()
    {
        const string sql = """
            SELECT COUNT(*)
            FROM pg_stat_activity
            WHERE state = 'active'
              AND now() - query_start > interval '30 seconds'
              AND query NOT ILIKE '%pg_stat_activity%'
            """;
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql);
    }

    public async Task<int> GetBlockedSessionCountAsync()
    {
        const string sql = "SELECT COUNT(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock'";
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql);
    }

    public async Task<int> GetTotalActiveSessionsAsync()
    {
        const string sql = "SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active'";
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql);
    }

    public async Task<long> GetTopDeadTupleCountAsync()
    {
        const string sql = "SELECT COALESCE(MAX(n_dead_tup), 0) FROM pg_stat_user_tables";
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<long>(sql);
    }

    public async Task<int> GetSlowQueryCountAsync()
    {
        // pg_stat_statements requires the extension; gracefully skip if absent
        const string sql = """
            SELECT COUNT(*)
            FROM pg_stat_statements
            WHERE mean_exec_time > 1000
            """;
        try
        {
            await using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<int>(sql);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            _logger.LogDebug("pg_stat_statements extension not available; slow query count skipped.");
            return 0;
        }
    }
}
