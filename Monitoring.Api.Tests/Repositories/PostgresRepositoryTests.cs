using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Api.Data.Entities;
using Monitoring.Api.Repositories;
using Monitoring.Api.Tests.Helpers;

namespace Monitoring.Api.Tests.Repositories;

public sealed class PostgresRepositoryTests : IDisposable
{
    private readonly TestMonitoringDbContext _context;
    private readonly PostgresRepository _repo;

    public PostgresRepositoryTests()
    {
        _context = InMemoryDbContextFactory.Create();
        _repo = new PostgresRepository(_context, NullLogger<PostgresRepository>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ── GetOverviewAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverviewAsync_ReturnsMappedDto()
    {
        _context.Overview.Add(new OverviewRow
        {
            LongRunningCount = 3,
            BlockedCount = 1,
            ActiveCount = 10,
            MaxDurationSeconds = 45.5
        });
        await _context.SaveChangesAsync();

        var result = await _repo.GetOverviewAsync();

        Assert.Equal(3, result.LongRunningQueryCount);
        Assert.Equal(1, result.BlockedSessionCount);
        Assert.Equal(10, result.TotalActiveSessions);
        Assert.Equal(45.5, result.MaxQueryDurationSeconds);
        Assert.True(result.Timestamp <= DateTimeOffset.UtcNow);
    }

    // ── GetLongRunningQueriesAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetLongRunningQueriesAsync_FiltersAboveThreshold()
    {
        _context.LongRunningQueries.AddRange(
            new LongRunningQueryRow { Pid = 1, State = "active", Query = "SELECT 1", DurationSeconds = 10, ApplicationName = "app1" },
            new LongRunningQueryRow { Pid = 2, State = "active", Query = "SELECT 2", DurationSeconds = 40, ApplicationName = "app2" },
            new LongRunningQueryRow { Pid = 3, State = "active", Query = "SELECT 3", DurationSeconds = 60, ApplicationName = "app3" }
        );
        await _context.SaveChangesAsync();

        var result = (await _repo.GetLongRunningQueriesAsync(thresholdSeconds: 30)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.DurationSeconds > 30));
    }

    [Fact]
    public async Task GetLongRunningQueriesAsync_OrdersByDurationDescending()
    {
        _context.LongRunningQueries.AddRange(
            new LongRunningQueryRow { Pid = 1, State = "active", Query = "Q1", DurationSeconds = 35, ApplicationName = "" },
            new LongRunningQueryRow { Pid = 2, State = "active", Query = "Q2", DurationSeconds = 100, ApplicationName = "" },
            new LongRunningQueryRow { Pid = 3, State = "active", Query = "Q3", DurationSeconds = 50, ApplicationName = "" }
        );
        await _context.SaveChangesAsync();

        var result = (await _repo.GetLongRunningQueriesAsync(thresholdSeconds: 0)).ToList();

        Assert.Equal(100, result[0].DurationSeconds);
        Assert.Equal(50, result[1].DurationSeconds);
        Assert.Equal(35, result[2].DurationSeconds);
    }

    [Fact]
    public async Task GetLongRunningQueriesAsync_DefaultThresholdIs30()
    {
        _context.LongRunningQueries.AddRange(
            new LongRunningQueryRow { Pid = 1, State = "active", Query = "Q1", DurationSeconds = 25, ApplicationName = "" },
            new LongRunningQueryRow { Pid = 2, State = "active", Query = "Q2", DurationSeconds = 31, ApplicationName = "" }
        );
        await _context.SaveChangesAsync();

        var result = (await _repo.GetLongRunningQueriesAsync()).ToList();

        Assert.Single(result);
        Assert.Equal(2, result[0].Pid);
    }

    [Fact]
    public async Task GetLongRunningQueriesAsync_LimitsTo50()
    {
        for (int i = 1; i <= 60; i++)
        {
            _context.LongRunningQueries.Add(new LongRunningQueryRow
            {
                Pid = i, State = "active", Query = $"Q{i}",
                DurationSeconds = 100 + i, ApplicationName = ""
            });
        }
        await _context.SaveChangesAsync();

        var result = (await _repo.GetLongRunningQueriesAsync(thresholdSeconds: 0)).ToList();

        Assert.Equal(50, result.Count);
    }

    [Fact]
    public async Task GetLongRunningQueriesAsync_ReturnsEmpty_WhenNoneAboveThreshold()
    {
        _context.LongRunningQueries.Add(new LongRunningQueryRow
        {
            Pid = 1, State = "active", Query = "Q1", DurationSeconds = 5, ApplicationName = ""
        });
        await _context.SaveChangesAsync();

        var result = (await _repo.GetLongRunningQueriesAsync(thresholdSeconds: 30)).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLongRunningQueriesAsync_MapsAllDtoProperties()
    {
        _context.LongRunningQueries.Add(new LongRunningQueryRow
        {
            Pid = 42, State = "active", Query = "SELECT now()",
            DurationSeconds = 99.9, ApplicationName = "test-app"
        });
        await _context.SaveChangesAsync();

        var result = (await _repo.GetLongRunningQueriesAsync(thresholdSeconds: 0)).Single();

        Assert.Equal(42, result.Pid);
        Assert.Equal("active", result.State);
        Assert.Equal("SELECT now()", result.Query);
        Assert.Equal(99.9, result.DurationSeconds);
        Assert.Equal("test-app", result.ApplicationName);
    }

    // ── GetBlockedSessionsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetBlockedSessionsAsync_ReturnsAllRows()
    {
        _context.BlockedSessions.AddRange(
            new BlockedSessionRow { BlockedPid = 10, BlockedQuery = "Q1", BlockingPid = 20, BlockingQuery = "Q2" },
            new BlockedSessionRow { BlockedPid = 30, BlockedQuery = "Q3", BlockingPid = 40, BlockingQuery = "Q4" }
        );
        await _context.SaveChangesAsync();

        var result = (await _repo.GetBlockedSessionsAsync()).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetBlockedSessionsAsync_MapsAllDtoProperties()
    {
        _context.BlockedSessions.Add(new BlockedSessionRow
        {
            BlockedPid = 10, BlockedQuery = "UPDATE t SET x=1",
            BlockingPid = 20, BlockingQuery = "DELETE FROM t"
        });
        await _context.SaveChangesAsync();

        var dto = (await _repo.GetBlockedSessionsAsync()).Single();

        Assert.Equal(10, dto.BlockedPid);
        Assert.Equal("UPDATE t SET x=1", dto.BlockedQuery);
        Assert.Equal(20, dto.BlockingPid);
        Assert.Equal("DELETE FROM t", dto.BlockingQuery);
    }

    [Fact]
    public async Task GetBlockedSessionsAsync_ReturnsEmpty_WhenNoBlocks()
    {
        var result = (await _repo.GetBlockedSessionsAsync()).ToList();

        Assert.Empty(result);
    }

    // ── GetDeadTuplesAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDeadTuplesAsync_FiltersAboveMinimum()
    {
        _context.DeadTuples.AddRange(
            new DeadTuplesRow { SchemaName = "public", TableName = "orders", DeadTupleCount = 50, LiveTupleCount = 1000 },
            new DeadTuplesRow { SchemaName = "public", TableName = "users", DeadTupleCount = 200, LiveTupleCount = 500 },
            new DeadTuplesRow { SchemaName = "public", TableName = "logs", DeadTupleCount = 500, LiveTupleCount = 10000 }
        );
        await _context.SaveChangesAsync();

        var result = (await _repo.GetDeadTuplesAsync(minDeadTuples: 100)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.DeadTupleCount >= 100));
    }

    [Fact]
    public async Task GetDeadTuplesAsync_OrdersByDeadTupleCountDescending()
    {
        _context.DeadTuples.AddRange(
            new DeadTuplesRow { SchemaName = "public", TableName = "t1", DeadTupleCount = 100, LiveTupleCount = 1 },
            new DeadTuplesRow { SchemaName = "public", TableName = "t2", DeadTupleCount = 500, LiveTupleCount = 1 },
            new DeadTuplesRow { SchemaName = "public", TableName = "t3", DeadTupleCount = 200, LiveTupleCount = 1 }
        );
        await _context.SaveChangesAsync();

        var result = (await _repo.GetDeadTuplesAsync(minDeadTuples: 0)).ToList();

        Assert.Equal(500, result[0].DeadTupleCount);
        Assert.Equal(200, result[1].DeadTupleCount);
        Assert.Equal(100, result[2].DeadTupleCount);
    }

    [Fact]
    public async Task GetDeadTuplesAsync_DefaultMinIs100()
    {
        _context.DeadTuples.AddRange(
            new DeadTuplesRow { SchemaName = "public", TableName = "t1", DeadTupleCount = 50, LiveTupleCount = 1 },
            new DeadTuplesRow { SchemaName = "public", TableName = "t2", DeadTupleCount = 150, LiveTupleCount = 1 }
        );
        await _context.SaveChangesAsync();

        var result = (await _repo.GetDeadTuplesAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("t2", result[0].TableName);
    }

    [Fact]
    public async Task GetDeadTuplesAsync_LimitsTo50()
    {
        for (int i = 1; i <= 60; i++)
        {
            _context.DeadTuples.Add(new DeadTuplesRow
            {
                SchemaName = "public", TableName = $"table_{i}",
                DeadTupleCount = 1000 + i, LiveTupleCount = 1
            });
        }
        await _context.SaveChangesAsync();

        var result = (await _repo.GetDeadTuplesAsync(minDeadTuples: 0)).ToList();

        Assert.Equal(50, result.Count);
    }

    [Fact]
    public async Task GetDeadTuplesAsync_MapsAllDtoProperties()
    {
        _context.DeadTuples.Add(new DeadTuplesRow
        {
            SchemaName = "inventory", TableName = "products",
            DeadTupleCount = 999, LiveTupleCount = 5000
        });
        await _context.SaveChangesAsync();

        var dto = (await _repo.GetDeadTuplesAsync(minDeadTuples: 0)).Single();

        Assert.Equal("inventory", dto.SchemaName);
        Assert.Equal("products", dto.TableName);
        Assert.Equal(999, dto.DeadTupleCount);
        Assert.Equal(5000, dto.LiveTupleCount);
    }

    // ── Background collector methods ──────────────────────────────────────────

    [Fact]
    public async Task GetLongRunningQueryCountAsync_CountsAbove30Seconds()
    {
        _context.LongRunningQueries.AddRange(
            new LongRunningQueryRow { Pid = 1, State = "active", Query = "Q1", DurationSeconds = 10, ApplicationName = "" },
            new LongRunningQueryRow { Pid = 2, State = "active", Query = "Q2", DurationSeconds = 31, ApplicationName = "" },
            new LongRunningQueryRow { Pid = 3, State = "active", Query = "Q3", DurationSeconds = 60, ApplicationName = "" }
        );
        await _context.SaveChangesAsync();

        var count = await _repo.GetLongRunningQueryCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetBlockedSessionCountAsync_CountsAllBlockedSessions()
    {
        _context.BlockedSessions.AddRange(
            new BlockedSessionRow { BlockedPid = 1, BlockedQuery = "", BlockingPid = 2, BlockingQuery = "" },
            new BlockedSessionRow { BlockedPid = 3, BlockedQuery = "", BlockingPid = 4, BlockingQuery = "" },
            new BlockedSessionRow { BlockedPid = 5, BlockedQuery = "", BlockingPid = 6, BlockingQuery = "" }
        );
        await _context.SaveChangesAsync();

        var count = await _repo.GetBlockedSessionCountAsync();

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetTotalActiveSessionsAsync_CountsAllActiveSessions()
    {
        _context.LongRunningQueries.AddRange(
            new LongRunningQueryRow { Pid = 1, State = "active", Query = "Q1", DurationSeconds = 5, ApplicationName = "" },
            new LongRunningQueryRow { Pid = 2, State = "active", Query = "Q2", DurationSeconds = 50, ApplicationName = "" }
        );
        await _context.SaveChangesAsync();

        var count = await _repo.GetTotalActiveSessionsAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetTopDeadTupleCountAsync_ReturnsMaxDeadTupleCount()
    {
        _context.DeadTuples.AddRange(
            new DeadTuplesRow { SchemaName = "public", TableName = "t1", DeadTupleCount = 100, LiveTupleCount = 1 },
            new DeadTuplesRow { SchemaName = "public", TableName = "t2", DeadTupleCount = 999, LiveTupleCount = 1 },
            new DeadTuplesRow { SchemaName = "public", TableName = "t3", DeadTupleCount = 50, LiveTupleCount = 1 }
        );
        await _context.SaveChangesAsync();

        var max = await _repo.GetTopDeadTupleCountAsync();

        Assert.Equal(999L, max);
    }

    [Fact]
    public async Task GetTopDeadTupleCountAsync_ReturnsZero_WhenNoRows()
    {
        var max = await _repo.GetTopDeadTupleCountAsync();

        Assert.Equal(0L, max);
    }

    [Fact]
    public async Task GetSlowQueryCountAsync_CountsAbove1000ms()
    {
        _context.SlowQueries.AddRange(
            new SlowQueryRow { MeanExecTime = 500 },
            new SlowQueryRow { MeanExecTime = 1500 },
            new SlowQueryRow { MeanExecTime = 2000 }
        );
        await _context.SaveChangesAsync();

        var count = await _repo.GetSlowQueryCountAsync();

        Assert.Equal(2, count);
    }
}
