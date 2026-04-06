using Monitoring.Api.Data.Entities;

namespace Monitoring.Api.Tests.Data.Entities;

/// <summary>
/// Entities are mutable classes with settable properties (EF Core requirement).
/// These tests verify default values and that all properties can be set —
/// catching accidental removal or type changes.
/// </summary>
public sealed class EntityTests
{
    // ── Order ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Order_DefaultStatus_IsPending()
    {
        var order = new Order();
        Assert.Equal("pending", order.Status);
    }

    [Fact]
    public void Order_AllProperties_CanBeSet()
    {
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id = 1,
            CustomerId = 42,
            Status = "shipped",
            Total = 99.95m,
            CreatedAt = now
        };

        Assert.Equal(1, order.Id);
        Assert.Equal(42, order.CustomerId);
        Assert.Equal("shipped", order.Status);
        Assert.Equal(99.95m, order.Total);
        Assert.Equal(now, order.CreatedAt);
    }

    [Fact]
    public void Order_Total_CanBeNull()
    {
        var order = new Order { Total = null };
        Assert.Null(order.Total);
    }

    // ── LongRunningQueryRow ───────────────────────────────────────────────────

    [Fact]
    public void LongRunningQueryRow_DefaultStrings_AreEmpty()
    {
        var row = new LongRunningQueryRow();
        Assert.Equal("", row.State);
        Assert.Equal("", row.Query);
        Assert.Equal("", row.ApplicationName);
    }

    [Fact]
    public void LongRunningQueryRow_AllProperties_CanBeSet()
    {
        var row = new LongRunningQueryRow
        {
            Pid = 123,
            State = "active",
            Query = "SELECT 1",
            DurationSeconds = 45.5,
            ApplicationName = "myapp"
        };

        Assert.Equal(123, row.Pid);
        Assert.Equal("active", row.State);
        Assert.Equal("SELECT 1", row.Query);
        Assert.Equal(45.5, row.DurationSeconds);
        Assert.Equal("myapp", row.ApplicationName);
    }

    // ── BlockedSessionRow ─────────────────────────────────────────────────────

    [Fact]
    public void BlockedSessionRow_DefaultStrings_AreEmpty()
    {
        var row = new BlockedSessionRow();
        Assert.Equal("", row.BlockedQuery);
        Assert.Equal("", row.BlockingQuery);
    }

    [Fact]
    public void BlockedSessionRow_AllProperties_CanBeSet()
    {
        var row = new BlockedSessionRow
        {
            BlockedPid = 10,
            BlockedQuery = "Q1",
            BlockingPid = 20,
            BlockingQuery = "Q2"
        };

        Assert.Equal(10, row.BlockedPid);
        Assert.Equal("Q1", row.BlockedQuery);
        Assert.Equal(20, row.BlockingPid);
        Assert.Equal("Q2", row.BlockingQuery);
    }

    // ── DeadTuplesRow ─────────────────────────────────────────────────────────

    [Fact]
    public void DeadTuplesRow_DefaultStrings_AreEmpty()
    {
        var row = new DeadTuplesRow();
        Assert.Equal("", row.SchemaName);
        Assert.Equal("", row.TableName);
    }

    [Fact]
    public void DeadTuplesRow_AllProperties_CanBeSet()
    {
        var row = new DeadTuplesRow
        {
            SchemaName = "public",
            TableName = "orders",
            DeadTupleCount = 500,
            LiveTupleCount = 10000
        };

        Assert.Equal("public", row.SchemaName);
        Assert.Equal("orders", row.TableName);
        Assert.Equal(500, row.DeadTupleCount);
        Assert.Equal(10000, row.LiveTupleCount);
    }

    // ── OverviewRow ───────────────────────────────────────────────────────────

    [Fact]
    public void OverviewRow_Defaults_AreZero()
    {
        var row = new OverviewRow();
        Assert.Equal(0, row.LongRunningCount);
        Assert.Equal(0, row.BlockedCount);
        Assert.Equal(0, row.ActiveCount);
        Assert.Equal(0.0, row.MaxDurationSeconds);
    }

    [Fact]
    public void OverviewRow_AllProperties_CanBeSet()
    {
        var row = new OverviewRow
        {
            LongRunningCount = 5,
            BlockedCount = 2,
            ActiveCount = 20,
            MaxDurationSeconds = 120.5
        };

        Assert.Equal(5, row.LongRunningCount);
        Assert.Equal(2, row.BlockedCount);
        Assert.Equal(20, row.ActiveCount);
        Assert.Equal(120.5, row.MaxDurationSeconds);
    }

    // ── SlowQueryRow ──────────────────────────────────────────────────────────

    [Fact]
    public void SlowQueryRow_Default_IsZero()
    {
        var row = new SlowQueryRow();
        Assert.Equal(0.0, row.MeanExecTime);
    }

    [Fact]
    public void SlowQueryRow_MeanExecTime_CanBeSet()
    {
        var row = new SlowQueryRow { MeanExecTime = 1500.75 };
        Assert.Equal(1500.75, row.MeanExecTime);
    }
}
