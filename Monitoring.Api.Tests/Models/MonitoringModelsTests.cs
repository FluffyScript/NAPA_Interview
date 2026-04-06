using Monitoring.Api.Models;

namespace Monitoring.Api.Tests.Models;

/// <summary>
/// Verifies that DTO records are correctly constructed and exhibit
/// value equality — important because routes return these directly as JSON.
/// </summary>
public sealed class MonitoringModelsTests
{
    // ── OverviewDto ───────────────────────────────────────────────────────────

    [Fact]
    public void OverviewDto_PropertiesAreSet()
    {
        var ts = DateTimeOffset.UtcNow;
        var dto = new OverviewDto(3, 1, 10, 45.5, ts);

        Assert.Equal(3, dto.LongRunningQueryCount);
        Assert.Equal(1, dto.BlockedSessionCount);
        Assert.Equal(10, dto.TotalActiveSessions);
        Assert.Equal(45.5, dto.MaxQueryDurationSeconds);
        Assert.Equal(ts, dto.Timestamp);
    }

    [Fact]
    public void OverviewDto_ValueEquality()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new OverviewDto(1, 2, 3, 4.0, ts);
        var b = new OverviewDto(1, 2, 3, 4.0, ts);

        Assert.Equal(a, b);
    }

    // ── LongRunningQueryDto ───────────────────────────────────────────────────

    [Fact]
    public void LongRunningQueryDto_PropertiesAreSet()
    {
        var dto = new LongRunningQueryDto(42, "active", "SELECT 1", 99.9, "myapp");

        Assert.Equal(42, dto.Pid);
        Assert.Equal("active", dto.State);
        Assert.Equal("SELECT 1", dto.Query);
        Assert.Equal(99.9, dto.DurationSeconds);
        Assert.Equal("myapp", dto.ApplicationName);
    }

    [Fact]
    public void LongRunningQueryDto_ValueEquality()
    {
        var a = new LongRunningQueryDto(1, "active", "Q", 10.0, "app");
        var b = new LongRunningQueryDto(1, "active", "Q", 10.0, "app");

        Assert.Equal(a, b);
    }

    // ── BlockedSessionDto ─────────────────────────────────────────────────────

    [Fact]
    public void BlockedSessionDto_PropertiesAreSet()
    {
        var dto = new BlockedSessionDto(10, "UPDATE t", 20, "DELETE t");

        Assert.Equal(10, dto.BlockedPid);
        Assert.Equal("UPDATE t", dto.BlockedQuery);
        Assert.Equal(20, dto.BlockingPid);
        Assert.Equal("DELETE t", dto.BlockingQuery);
    }

    [Fact]
    public void BlockedSessionDto_ValueEquality()
    {
        var a = new BlockedSessionDto(1, "Q1", 2, "Q2");
        var b = new BlockedSessionDto(1, "Q1", 2, "Q2");

        Assert.Equal(a, b);
    }

    // ── DeadTuplesDto ─────────────────────────────────────────────────────────

    [Fact]
    public void DeadTuplesDto_PropertiesAreSet()
    {
        var dto = new DeadTuplesDto("public", "orders", 500, 10000);

        Assert.Equal("public", dto.SchemaName);
        Assert.Equal("orders", dto.TableName);
        Assert.Equal(500, dto.DeadTupleCount);
        Assert.Equal(10000, dto.LiveTupleCount);
    }

    [Fact]
    public void DeadTuplesDto_ValueEquality()
    {
        var a = new DeadTuplesDto("s", "t", 1, 2);
        var b = new DeadTuplesDto("s", "t", 1, 2);

        Assert.Equal(a, b);
    }

    // ── SystemMetricsDto ──────────────────────────────────────────────────────

    [Fact]
    public void SystemMetricsDto_PropertiesAreSet()
    {
        var ts = DateTimeOffset.UtcNow;
        var dto = new SystemMetricsDto(
            CpuPercent: 23.4,
            Cpu: "23.4%  (4 cores)",
            CpuCoreCount: 4,
            MemoryTotalBytes: 17_179_869_184,
            MemoryTotal: "16.0 GB",
            MemoryAvailableBytes: 8_589_934_592,
            MemoryAvailable: "8.0 GB",
            MemoryUsedBytes: 8_589_934_592,
            MemoryUsed: "8.0 GB",
            MemoryUsage: "50.0% used",
            Source: "node_exporter",
            Timestamp: ts
        );

        Assert.Equal(23.4, dto.CpuPercent);
        Assert.Equal("23.4%  (4 cores)", dto.Cpu);
        Assert.Equal(4, dto.CpuCoreCount);
        Assert.Equal(17_179_869_184, dto.MemoryTotalBytes);
        Assert.Equal("16.0 GB", dto.MemoryTotal);
        Assert.Equal(8_589_934_592, dto.MemoryAvailableBytes);
        Assert.Equal("8.0 GB", dto.MemoryAvailable);
        Assert.Equal(8_589_934_592, dto.MemoryUsedBytes);
        Assert.Equal("8.0 GB", dto.MemoryUsed);
        Assert.Equal("50.0% used", dto.MemoryUsage);
        Assert.Equal("node_exporter", dto.Source);
        Assert.Equal(ts, dto.Timestamp);
    }

    [Fact]
    public void SystemMetricsDto_ValueEquality()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new SystemMetricsDto(10.0, "10.0%  (2 cores)", 2, 1000, "1000 B", 500, "500 B", 500, "500 B", "50.0% used", "node_exporter", ts);
        var b = new SystemMetricsDto(10.0, "10.0%  (2 cores)", 2, 1000, "1000 B", 500, "500 B", 500, "500 B", "50.0% used", "node_exporter", ts);

        Assert.Equal(a, b);
    }
}
