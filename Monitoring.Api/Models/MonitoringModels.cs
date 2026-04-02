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
    // ── CPU ──────────────────────────────────────────────────────────────────
    double ProcessCpuPercent,
    string ProcessCpu,               // e.g. "12.3%  (8 logical cores)"

    // ── Process memory ────────────────────────────────────────────────────────
    long   ProcessMemoryBytes,
    string ProcessMemory,            // e.g. "128.4 MB"

    // ── System memory ─────────────────────────────────────────────────────────
    long   SystemMemoryTotalBytes,
    string SystemMemoryTotal,        // e.g. "15.9 GB"

    long?  SystemMemoryAvailableBytes,
    string? SystemMemoryAvailable,   // e.g. "8.2 GB"   — Linux only, null on Windows

    long?  SystemMemoryUsedBytes,
    string? SystemMemoryUsed,        // e.g. "7.7 GB"   — Linux only, null on Windows
    string? SystemMemoryUsage,       // e.g. "48.4% used" — Linux only, null on Windows

    // ── Meta ─────────────────────────────────────────────────────────────────
    int    ProcessorCount,
    string Platform,
    DateTimeOffset Timestamp
);
