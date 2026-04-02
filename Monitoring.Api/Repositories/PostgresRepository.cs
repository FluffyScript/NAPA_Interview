using Microsoft.EntityFrameworkCore;
using Monitoring.Api.Data;
using Monitoring.Api.Models;
using Npgsql;

namespace Monitoring.Api.Repositories;

public sealed class PostgresRepository
{
    private readonly MonitoringDbContext _context;
    private readonly ILogger<PostgresRepository> _logger;

    public PostgresRepository(MonitoringDbContext context, ILogger<PostgresRepository> logger)
    {
        _context = context;
        _logger  = logger;
    }

    public async Task<OverviewDto> GetOverviewAsync()
    {
        var row = await _context.Overview
            .FromSql($"""
                SELECT
                    CAST((
                        SELECT COUNT(*) FROM pg_stat_activity
                        WHERE state = 'active'
                          AND now() - query_start > interval '30 seconds'
                          AND query NOT ILIKE '%pg_stat_activity%'
                    ) AS int) AS long_running_count,
                    CAST((
                        SELECT COUNT(*) FROM pg_stat_activity
                        WHERE wait_event_type = 'Lock'
                    ) AS int) AS blocked_count,
                    CAST((
                        SELECT COUNT(*) FROM pg_stat_activity
                        WHERE state = 'active'
                    ) AS int) AS active_count,
                    CAST(COALESCE((
                        SELECT EXTRACT(EPOCH FROM MAX(now() - query_start))
                        FROM pg_stat_activity
                        WHERE state = 'active'
                          AND query NOT ILIKE '%pg_stat_activity%'
                    ), 0) AS double precision) AS max_duration_seconds
                """)
            .SingleAsync();

        return new OverviewDto(
            LongRunningQueryCount:  row.LongRunningCount,
            BlockedSessionCount:    row.BlockedCount,
            TotalActiveSessions:    row.ActiveCount,
            MaxQueryDurationSeconds: row.MaxDurationSeconds,
            Timestamp:              DateTimeOffset.UtcNow
        );
    }

    public async Task<IEnumerable<LongRunningQueryDto>> GetLongRunningQueriesAsync(int thresholdSeconds = 30)
    {
        var rows = await _context.LongRunningQueries
            .FromSql($"""
                SELECT
                    pid,
                    state,
                    query,
                    CAST(EXTRACT(EPOCH FROM (now() - query_start)) AS double precision) AS duration_seconds,
                    COALESCE(application_name, '') AS application_name
                FROM pg_stat_activity
                WHERE state = 'active'
                  AND now() - query_start > make_interval(secs => {thresholdSeconds})
                  AND query NOT ILIKE '%pg_stat_activity%'
                ORDER BY duration_seconds DESC
                LIMIT 50
                """)
            .ToListAsync();

        return rows.Select(r => new LongRunningQueryDto(
            Pid:             r.Pid,
            State:           r.State,
            Query:           r.Query,
            DurationSeconds: r.DurationSeconds,
            ApplicationName: r.ApplicationName
        ));
    }

    public async Task<IEnumerable<BlockedSessionDto>> GetBlockedSessionsAsync()
    {
        var rows = await _context.BlockedSessions
            .FromSql($"""
                SELECT
                    blocked.pid   AS blocked_pid,
                    blocked.query AS blocked_query,
                    blocking.pid  AS blocking_pid,
                    blocking.query AS blocking_query
                FROM pg_stat_activity AS blocked
                JOIN pg_stat_activity AS blocking
                    ON blocking.pid = ANY(pg_blocking_pids(blocked.pid))
                WHERE cardinality(pg_blocking_pids(blocked.pid)) > 0
                """)
            .ToListAsync();

        return rows.Select(r => new BlockedSessionDto(
            BlockedPid:    r.BlockedPid,
            BlockedQuery:  r.BlockedQuery,
            BlockingPid:   r.BlockingPid,
            BlockingQuery: r.BlockingQuery
        ));
    }

    public async Task<IEnumerable<DeadTuplesDto>> GetDeadTuplesAsync(int minDeadTuples = 100)
    {
        var rows = await _context.DeadTuples
            .FromSql($"""
                SELECT
                    schemaname AS schema_name,
                    relname    AS table_name,
                    n_dead_tup AS dead_tuple_count,
                    n_live_tup AS live_tuple_count
                FROM pg_stat_user_tables
                WHERE n_dead_tup >= {minDeadTuples}
                ORDER BY n_dead_tup DESC
                LIMIT 50
                """)
            .ToListAsync();

        return rows.Select(r => new DeadTuplesDto(
            SchemaName:     r.SchemaName,
            TableName:      r.TableName,
            DeadTupleCount: r.DeadTupleCount,
            LiveTupleCount: r.LiveTupleCount
        ));
    }

    // ── Methods used by the background collector ──────────────────────────────

    public async Task<int> GetLongRunningQueryCountAsync() =>
        (int)await _context.Counts
            .FromSql($"""
                SELECT CAST(COUNT(*) AS bigint) AS count
                FROM pg_stat_activity
                WHERE state = 'active'
                  AND now() - query_start > interval '30 seconds'
                  AND query NOT ILIKE '%pg_stat_activity%'
                """)
            .Select(r => r.Count)
            .SingleAsync();

    public async Task<int> GetBlockedSessionCountAsync() =>
        (int)await _context.Counts
            .FromSql($"SELECT CAST(COUNT(*) AS bigint) AS count FROM pg_stat_activity WHERE wait_event_type = 'Lock'")
            .Select(r => r.Count)
            .SingleAsync();

    public async Task<int> GetTotalActiveSessionsAsync() =>
        (int)await _context.Counts
            .FromSql($"SELECT CAST(COUNT(*) AS bigint) AS count FROM pg_stat_activity WHERE state = 'active'")
            .Select(r => r.Count)
            .SingleAsync();

    public async Task<long> GetTopDeadTupleCountAsync() =>
        await _context.DeadTuples
            .FromSql($"""
                SELECT schemaname AS schema_name, relname AS table_name,
                       n_dead_tup AS dead_tuple_count, n_live_tup AS live_tuple_count
                FROM pg_stat_user_tables
                """)
            .MaxAsync(r => r.DeadTupleCount);

    public async Task<int> GetSlowQueryCountAsync()
    {
        try
        {
            return (int)await _context.Counts
                .FromSql($"SELECT CAST(COUNT(*) AS bigint) AS count FROM pg_stat_statements WHERE mean_exec_time > 1000")
                .Select(r => r.Count)
                .SingleAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            _logger.LogDebug("pg_stat_statements extension not available; slow query count skipped.");
            return 0;
        }
    }
}
