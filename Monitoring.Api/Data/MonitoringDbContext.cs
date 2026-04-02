using Microsoft.EntityFrameworkCore;
using Monitoring.Api.Data.Entities;

namespace Monitoring.Api.Data;

public class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : base(options) { }

    // ── Real table ────────────────────────────────────────────────────────────
    public DbSet<Order> Orders => Set<Order>();

    // ── Keyless sets for pg_stat_* raw SQL queries ────────────────────────────
    // These are never tracked or migrated — they exist purely as query return types.
    public DbSet<LongRunningQueryRow> LongRunningQueries => Set<LongRunningQueryRow>();
    public DbSet<BlockedSessionRow>   BlockedSessions    => Set<BlockedSessionRow>();
    public DbSet<DeadTuplesRow>       DeadTuples         => Set<DeadTuplesRow>();
    public DbSet<OverviewRow>         Overview           => Set<OverviewRow>();
    public DbSet<CountRow>            Counts             => Set<CountRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Orders — standard entity with a generated PK and a server-side default timestamp
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasDefaultValue("pending");
            e.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
        });

        // Keyless entities — not mapped to any table or view; only used with FromSql
        modelBuilder.Entity<LongRunningQueryRow>().HasNoKey();
        modelBuilder.Entity<BlockedSessionRow>().HasNoKey();
        modelBuilder.Entity<DeadTuplesRow>().HasNoKey();
        modelBuilder.Entity<OverviewRow>().HasNoKey();
        modelBuilder.Entity<CountRow>().HasNoKey();
    }
}
