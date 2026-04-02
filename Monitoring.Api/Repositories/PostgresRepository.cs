using Microsoft.EntityFrameworkCore;
using Monitoring.Api.Data;
using Monitoring.Api.Models;
using Npgsql;

namespace Monitoring.Api.Repositories;

/// <summary>
/// Translates DbContext queries into API-facing DTOs.
///
/// Rule: no SQL strings here — all queries are expressed as LINQ over DbSets.
/// The SQL that backs each keyless entity lives in <see cref="MonitoringDbContext.OnModelCreating"/>.
/// </summary>
public sealed class PostgresRepository
{
    private readonly MonitoringDbContext _context;
    private readonly ILogger<PostgresRepository> _logger;

    public PostgresRepository(MonitoringDbContext context, ILogger<PostgresRepository> logger)
    {
        _context = context;
        _logger  = logger;
    }

    // ── API-facing methods ────────────────────────────────────────────────────

    public async Task<OverviewDto> GetOverviewAsync()
    {
        var row = await _context.Overview.SingleAsync();

        return new OverviewDto(
            LongRunningQueryCount:   row.LongRunningCount,
            BlockedSessionCount:     row.BlockedCount,
            TotalActiveSessions:     row.ActiveCount,
            MaxQueryDurationSeconds: row.MaxDurationSeconds,
            Timestamp:               DateTimeOffset.UtcNow
        );
    }

    public async Task<IEnumerable<LongRunningQueryDto>> GetLongRunningQueriesAsync(int thresholdSeconds = 30)
    {
        var rows = await _context.LongRunningQueries
            .Where(q => q.DurationSeconds > thresholdSeconds)
            .OrderByDescending(q => q.DurationSeconds)
            .Take(50)
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
        var rows = await _context.BlockedSessions.ToListAsync();

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
            .Where(r => r.DeadTupleCount >= minDeadTuples)
            .OrderByDescending(r => r.DeadTupleCount)
            .Take(50)
            .ToListAsync();

        return rows.Select(r => new DeadTuplesDto(
            SchemaName:     r.SchemaName,
            TableName:      r.TableName,
            DeadTupleCount: r.DeadTupleCount,
            LiveTupleCount: r.LiveTupleCount
        ));
    }

    // ── Background collector methods ──────────────────────────────────────────

    public Task<int> GetLongRunningQueryCountAsync() =>
        _context.LongRunningQueries
            .CountAsync(q => q.DurationSeconds > 30);

    public Task<int> GetBlockedSessionCountAsync() =>
        _context.BlockedSessions.CountAsync();

    public Task<int> GetTotalActiveSessionsAsync() =>
        _context.LongRunningQueries.CountAsync();

    public async Task<long> GetTopDeadTupleCountAsync() =>
        await _context.DeadTuples.MaxAsync(r => (long?)r.DeadTupleCount) ?? 0L;

    public async Task<int> GetSlowQueryCountAsync()
    {
        try
        {
            return await _context.SlowQueries
                .CountAsync(q => q.MeanExecTime > 1000);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            _logger.LogDebug("pg_stat_statements extension not available; slow query count skipped.");
            return 0;
        }
    }
}
