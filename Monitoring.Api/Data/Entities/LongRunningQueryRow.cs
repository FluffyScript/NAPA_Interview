namespace Monitoring.Api.Data.Entities;

/// <summary>Keyless entity — maps to a raw SQL result over pg_stat_activity.</summary>
public class LongRunningQueryRow
{
    public int Pid { get; set; }
    public string State { get; set; } = "";
    public string Query { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string ApplicationName { get; set; } = "";
}
