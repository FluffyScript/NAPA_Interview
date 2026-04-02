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
    double ProcessCpuPercent,
    long ProcessMemoryBytes,
    long SystemMemoryTotalBytes,
    long? SystemMemoryAvailableBytes,
    int ProcessorCount,
    string Platform,
    DateTimeOffset Timestamp
);
