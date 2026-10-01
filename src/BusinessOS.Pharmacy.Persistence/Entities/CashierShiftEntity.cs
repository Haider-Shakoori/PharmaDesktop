namespace BusinessOS.Pharmacy.Persistence.Entities;
internal sealed class CashierShiftEntity
{
 public string Id{get;set;}="";public string StockLocationId{get;set;}="";public string UserId{get;set;}="";public DateOnly BusinessDate{get;set;}public string Status{get;set;}="open";
 public decimal OpeningCash{get;set;}public decimal? ExpectedCash{get;set;}public decimal? CountedCash{get;set;}public decimal? Variance{get;set;}public DateTimeOffset OpenedAt{get;set;}public DateTimeOffset? ClosedAt{get;set;}public string? ClosingNotes{get;set;}public DateTimeOffset CreatedAt{get;set;}public DateTimeOffset UpdatedAt{get;set;}public StockLocationEntity StockLocation{get;set;}=null!;
}
