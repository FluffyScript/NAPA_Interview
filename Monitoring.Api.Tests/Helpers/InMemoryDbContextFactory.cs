using Microsoft.EntityFrameworkCore;
using Monitoring.Api.Data;
using Monitoring.Api.Data.Entities;

namespace Monitoring.Api.Tests.Helpers;

/// <summary>
/// Creates an in-memory MonitoringDbContext for unit testing.
///
/// The real DbContext uses ToSqlQuery() for keyless entities which the InMemory
/// provider does not support. This derived context replaces those SQL-backed
/// keyless sets with simple in-memory collections that behave as regular DbSets,
/// letting us seed data and test repository LINQ without needing PostgreSQL.
/// </summary>
public static class InMemoryDbContextFactory
{
    public static TestMonitoringDbContext Create(string? dbName = null)
    {
        dbName ??= Guid.NewGuid().ToString();

        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new TestMonitoringDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}

/// <summary>
/// Test-only DbContext that overrides OnModelCreating to skip the ToSqlQuery()
/// calls (which the InMemory provider cannot handle) while keeping entity shapes intact.
/// </summary>
public class TestMonitoringDbContext : MonitoringDbContext
{
    public TestMonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Order — keep the same config as the real context
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasDefaultValue("pending");
        });

        // Keyless entities need a synthetic key for InMemory to work.
        // We give each one a shadow "Id" property so EF can track them.
        modelBuilder.Entity<LongRunningQueryRow>(e =>
        {
            e.HasKey(r => r.Pid);
        });

        modelBuilder.Entity<BlockedSessionRow>(e =>
        {
            e.HasKey(r => r.BlockedPid);
        });

        modelBuilder.Entity<DeadTuplesRow>(e =>
        {
            e.HasKey(r => r.TableName);
        });

        modelBuilder.Entity<OverviewRow>(e =>
        {
            e.Property<int>("Id");
            e.HasKey("Id");
        });

        modelBuilder.Entity<SlowQueryRow>(e =>
        {
            e.Property<int>("Id");
            e.HasKey("Id");
        });
    }
}
