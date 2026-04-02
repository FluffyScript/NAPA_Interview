namespace Monitoring.Api.Data.Entities;

/// <summary>Keyless entity — maps to a raw SQL result over pg_stat_user_tables.</summary>
public class DeadTuplesRow
{
    public string SchemaName { get; set; } = "";
    public string TableName { get; set; } = "";
    public long DeadTupleCount { get; set; }
    public long LiveTupleCount { get; set; }
}
