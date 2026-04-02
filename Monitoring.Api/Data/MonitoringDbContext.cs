using Microsoft.EntityFrameworkCore;
using Monitoring.Api.Data.Entities;

namespace Monitoring.Api.Data;

/// <summary>
/// Single EF Core DbContext for the monitoring API.
///
/// Responsibilities:
///   - Own the connection lifetime and transaction scope.
///   - Map the <see cref="Order"/> entity to its physical table.
///   - Define the SQL for every keyless read-only view via ToSqlQuery() so that
///     repositories can express all queries as plain LINQ without embedding SQL strings.
///
/// Rule: SQL belongs here. Repositories contain only LINQ.
/// </summary>
public class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : base(options) { }

    // ── Real table (tracked, migratable) ─────────────────────────────────────
    public DbSet<Order> Orders => Set<Order>();

    // ── Keyless read-only views (untracked, never migrated) ───────────────────
    public DbSet<LongRunningQueryRow> LongRunningQueries => Set<LongRunningQueryRow>();
    public DbSet<BlockedSessionRow>   BlockedSessions    => Set<BlockedSessionRow>();
    public DbSet<DeadTuplesRow>       DeadTuples         => Set<DeadTuplesRow>();
    public DbSet<OverviewRow>         Overview           => Set<OverviewRow>();
    public DbSet<SlowQueryRow>        SlowQueries        => Set<SlowQueryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Order ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasDefaultValue("pending");
            e.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
        });

        // ── LongRunningQueryRow ───────────────────────────────────────────────
        // Returns all active, non-monitoring sessions with their duration.
        // Repositories apply .Where() / .OrderBy() / .Take() on top via LINQ.
        modelBuilder.Entity<LongRunningQueryRow>().HasNoKey().ToSqlQuery("""
            SELECT
                pid,
                state,
                query,
                CAST(EXTRACT(EPOCH FROM (now() - query_start)) AS double precision) AS duration_seconds,
                COALESCE(application_name, '') AS application_name
            FROM pg_stat_activity
            WHERE state = 'active'
              AND query NOT ILIKE '%pg_stat_activity%'
            """);

        // ── BlockedSessionRow ─────────────────────────────────────────────────
        // Returns every (blocked, blocker) pair currently held by a lock.
        modelBuilder.Entity<BlockedSessionRow>().HasNoKey().ToSqlQuery("""
            SELECT
                blocked.pid    AS blocked_pid,
                blocked.query  AS blocked_query,
                blocking.pid   AS blocking_pid,
                blocking.query AS blocking_query
            FROM pg_stat_activity AS blocked
            JOIN pg_stat_activity AS blocking
                ON blocking.pid = ANY(pg_blocking_pids(blocked.pid))
            WHERE cardinality(pg_blocking_pids(blocked.pid)) > 0
            """);

        // ── DeadTuplesRow ─────────────────────────────────────────────────────
        // Returns dead and live tuple counts for every user table.
        // Repositories filter by minimum threshold and apply ordering via LINQ.
        modelBuilder.Entity<DeadTuplesRow>().HasNoKey().ToSqlQuery("""
            SELECT
                schemaname AS schema_name,
                relname    AS table_name,
                n_dead_tup AS dead_tuple_count,
                n_live_tup AS live_tuple_count
            FROM pg_stat_user_tables
            """);

        // ── OverviewRow ───────────────────────────────────────────────────────
        // Single-row aggregate summary — always returns exactly one row.
        modelBuilder.Entity<OverviewRow>().HasNoKey().ToSqlQuery("""
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
            """);

        // ── SlowQueryRow ──────────────────────────────────────────────────────
        // Requires the pg_stat_statements extension.
        // Repositories catch PostgresException(42P01) if the extension is absent.
        modelBuilder.Entity<SlowQueryRow>().HasNoKey().ToSqlQuery(
            "SELECT mean_exec_time FROM pg_stat_statements");
    }
}
