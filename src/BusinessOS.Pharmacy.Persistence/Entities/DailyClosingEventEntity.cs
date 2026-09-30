namespace BusinessOS.Pharmacy.Persistence.Entities;
internal sealed class DailyClosingEventEntity
{
 public string Id{get;set;}="";public string DailyClosingId{get;set;}="";public string EventType{get;set;}="";public string? ActorId{get;set;}public string? Reason{get;set;}public string? SnapshotJson{get;set;}public DateTimeOffset OccurredAt{get;set;}public DateTimeOffset CreatedAt{get;set;}public DateTimeOffset UpdatedAt{get;set;}public DailyClosingEntity Closing{get;set;}=null!;
}
