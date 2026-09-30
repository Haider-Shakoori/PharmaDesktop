namespace BusinessOS.Pharmacy.Persistence.Entities;
internal sealed class DailyClosingEntity
{
 public string Id{get;set;}="";public string StockLocationId{get;set;}="";public DateOnly BusinessDate{get;set;}public string Status{get;set;}="draft";
 public decimal GrossSales{get;set;}public decimal DiscountTotal{get;set;}public decimal ReturnsTotal{get;set;}public decimal CashCollected{get;set;}public decimal BankCollected{get;set;}public decimal MobileCollected{get;set;}public decimal CreditSales{get;set;}public decimal OpeningCash{get;set;}public decimal ExpectedCash{get;set;}public decimal? CountedCash{get;set;}public decimal? Variance{get;set;}
 public string? FinalizedBy{get;set;}public string? ApprovedBy{get;set;}public string? ReopenedBy{get;set;}public DateTimeOffset? FinalizedAt{get;set;}public DateTimeOffset? ApprovedAt{get;set;}public DateTimeOffset? ReopenedAt{get;set;}public string? ClosingNotes{get;set;}public string? ReopenReason{get;set;}public DateTimeOffset CreatedAt{get;set;}public DateTimeOffset UpdatedAt{get;set;}
 public StockLocationEntity StockLocation{get;set;}=null!;public List<DailyClosingEventEntity> Events{get;set;}=[];
}
