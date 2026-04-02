namespace Monitoring.Api.Data.Entities;

/// <summary>
/// Keyless entity mapped to pg_stat_statements.
/// Requires the pg_stat_statements extension — gracefully absent if the extension is not installed.
/// </summary>
public class SlowQueryRow
{
    public double MeanExecTime { get; set; }
}
