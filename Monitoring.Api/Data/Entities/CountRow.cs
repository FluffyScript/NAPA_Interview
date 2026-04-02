namespace Monitoring.Api.Data.Entities;

/// <summary>
/// Keyless entity used for scalar COUNT(*) queries.
/// SQL must alias the result column as "count".
/// </summary>
public class CountRow
{
    public long Count { get; set; }
}
