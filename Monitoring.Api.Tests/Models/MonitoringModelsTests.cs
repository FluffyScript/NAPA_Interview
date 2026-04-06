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
            ProcessCpuPercent: 12.3,
            ProcessCpu: "12.3%  (8 logical cores)",
            ProcessMemoryBytes: 134_217_728,
            ProcessMemory: "128.0 MB",
            SystemMemoryTotalBytes: 17_179_869_184,
            SystemMemoryTotal: "16.0 GB",
            SystemMemoryAvailableBytes: 8_589_934_592,
            SystemMemoryAvailable: "8.0 GB",
            SystemMemoryUsedBytes: 8_589_934_592,
            SystemMemoryUsed: "8.0 GB",
            SystemMemoryUsage: "50.0% used",
            ProcessorCount: 8,
            Platform: "Windows 10",
            Timestamp: ts
        );

        Assert.Equal(12.3, dto.ProcessCpuPercent);
        Assert.Equal("12.3%  (8 logical cores)", dto.ProcessCpu);
        Assert.Equal(134_217_728, dto.ProcessMemoryBytes);
        Assert.Equal("128.0 MB", dto.ProcessMemory);
        Assert.Equal(17_179_869_184, dto.SystemMemoryTotalBytes);
        Assert.Equal("16.0 GB", dto.SystemMemoryTotal);
        Assert.Equal(8_589_934_592, dto.SystemMemoryAvailableBytes);
        Assert.Equal("8.0 GB", dto.SystemMemoryAvailable);
        Assert.Equal(8_589_934_592, dto.SystemMemoryUsedBytes);
        Assert.Equal("8.0 GB", dto.SystemMemoryUsed);
        Assert.Equal("50.0% used", dto.SystemMemoryUsage);
        Assert.Equal(8, dto.ProcessorCount);
        Assert.Equal("Windows 10", dto.Platform);
        Assert.Equal(ts, dto.Timestamp);
    }

    [Fact]
    public void SystemMetricsDto_NullableFields_CanBeNull()
    {
        var ts = DateTimeOffset.UtcNow;
        var dto = new SystemMetricsDto(
            ProcessCpuPercent: 0,
            ProcessCpu: "0.0%",
            ProcessMemoryBytes: 0,
            ProcessMemory: "0 B",
            SystemMemoryTotalBytes: 0,
            SystemMemoryTotal: "N/A",
            SystemMemoryAvailableBytes: null,
            SystemMemoryAvailable: null,
            SystemMemoryUsedBytes: null,
            SystemMemoryUsed: null,
            SystemMemoryUsage: null,
            ProcessorCount: 1,
            Platform: "test",
            Timestamp: ts
        );

        Assert.Null(dto.SystemMemoryAvailableBytes);
        Assert.Null(dto.SystemMemoryAvailable);
        Assert.Null(dto.SystemMemoryUsedBytes);
        Assert.Null(dto.SystemMemoryUsed);
        Assert.Null(dto.SystemMemoryUsage);
    }
}
