using System.ComponentModel.DataAnnotations.Schema;

namespace Monitoring.Api.Data.Entities;

[Table("orders")]
public class Order
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; } = "pending";
    public decimal? Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
