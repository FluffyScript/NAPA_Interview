namespace Monitoring.Api.Data.Entities;

/// <summary>Keyless entity — maps to the single-row overview summary query.</summary>
public class OverviewRow
{
    public int LongRunningCount { get; set; }
    public int BlockedCount { get; set; }
    public int ActiveCount { get; set; }
    public double MaxDurationSeconds { get; set; }
}
