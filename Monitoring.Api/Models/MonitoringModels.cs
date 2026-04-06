namespace Monitoring.Api.Models;

public record OverviewDto(
    int LongRunningQueryCount,
    int BlockedSessionCount,
    int TotalActiveSessions,
    double MaxQueryDurationSeconds,
    DateTimeOffset Timestamp
);

public record LongRunningQueryDto(
    int Pid,
    string State,
    string Query,
    double DurationSeconds,
    string ApplicationName
);

public record BlockedSessionDto(
    int BlockedPid,
    string BlockedQuery,
    int BlockingPid,
    string BlockingQuery
);

public record DeadTuplesDto(
    string SchemaName,
    string TableName,
    long DeadTupleCount,
    long LiveTupleCount
);

public record SystemMetricsDto(
    // ── DB Host CPU ─────────────────────────────────────────────────────────
    double CpuPercent,
    string Cpu,                      // e.g. "23.4%  (4 cores)"
    int    CpuCoreCount,

    // ── DB Host Memory ──────────────────────────────────────────────────────
    long   MemoryTotalBytes,
    string MemoryTotal,              // e.g. "15.9 GB"

    long   MemoryAvailableBytes,
    string MemoryAvailable,          // e.g. "8.2 GB"

    long   MemoryUsedBytes,
    string MemoryUsed,               // e.g. "7.7 GB"
    string MemoryUsage,              // e.g. "48.4% used"

    // ── Meta ─────────────────────────────────────────────────────────────────
    string Source,                    // "node_exporter"
    DateTimeOffset Timestamp
);
