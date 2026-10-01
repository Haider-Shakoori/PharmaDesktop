namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class SaleReturnEntity
{
    public string Id { get; set; } = string.Empty;
    public string ReturnNumber { get; set; } = string.Empty;
    public string SaleId { get; set; } = string.Empty;
    public string StockLocationId { get; set; } = string.Empty;
    public DateOnly BusinessDate { get; set; }
    public string Status { get; set; } = "processing";
    public decimal RefundTotal { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public SaleEntity Sale { get; set; } = null!;
    public StockLocationEntity StockLocation { get; set; } = null!;
    public List<SaleReturnLineEntity> Lines { get; set; } = [];
    public List<SaleReturnRefundEntity> Refunds { get; set; } = [];
}
