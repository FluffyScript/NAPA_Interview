namespace Monitoring.Api.Data.Entities;

/// <summary>Keyless entity — maps to a raw SQL result joining pg_stat_activity on blocking pids.</summary>
public class BlockedSessionRow
{
    public int BlockedPid { get; set; }
    public string BlockedQuery { get; set; } = "";
    public int BlockingPid { get; set; }
    public string BlockingQuery { get; set; } = "";
}
