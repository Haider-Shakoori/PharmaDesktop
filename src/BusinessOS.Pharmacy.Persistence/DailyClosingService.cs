using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

public sealed class DailyClosingService:IDailyClosingService
{
 private readonly IDbContextFactory<PharmacyDbContext> _factory;private readonly IPermissionAuthorizer _permissions;private readonly IUserSessionService _sessions;private readonly IClock _clock;private readonly AccountingProvisioner _accounts;private readonly LedgerPostingService _ledger;
 public DailyClosingService(IDbContextFactory<PharmacyDbContext> factory,IPermissionAuthorizer permissions,IUserSessionService sessions,IClock clock,AccountingProvisioner accounts,LedgerPostingService ledger){_factory=factory;_permissions=permissions;_sessions=sessions;_clock=clock;_accounts=accounts;_ledger=ledger;}
 public DateOnly BusinessDate()=>StockLedger.BusinessDate(_clock.UtcNow);

 public async Task<DailyClosingReferenceData> GetReferenceDataAsync(CancellationToken ct=default)
 {
  _permissions.Demand("daily_closing.perform");await using var c=await _factory.CreateDbContextAsync(ct);
  var rows=await c.Set<StockLocationEntity>().AsNoTracking().Include(x=>x.Branch).Where(x=>x.IsActive&&x.Branch.IsActive).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Name).Select(x=>new DailyClosingLocationItem(x.Id,x.Name,x.Branch.Name,x.IsDefault)).ToListAsync(ct);return new(rows);
 }

 public async Task<DailyClosingWorkspace> GetWorkspaceAsync(string locationId,DateOnly? date=null,CancellationToken ct=default)
 {
  _permissions.Demand("daily_closing.perform");var d=date??BusinessDate();await using var c=await _factory.CreateDbContextAsync(ct);
  await RequireLocation(c,locationId,ct);var snapshot=await Snapshot(c,locationId,d,ct);
  var closing=await ClosingQuery(c).AsNoTracking().SingleOrDefaultAsync(x=>x.StockLocationId==locationId&&x.BusinessDate==d,ct);
  var user=_sessions.Current?.UserId;var shifts=await c.Set<CashierShiftEntity>().AsNoTracking().Where(x=>x.StockLocationId==locationId&&x.BusinessDate==d).OrderByDescending(x=>x.OpenedAt).ToListAsync(ct);
  return new(snapshot,closing is null?null:Map(closing),shifts.FirstOrDefault(x=>x.UserId==user&&x.Status=="open") is { } mine?Map(mine):null,shifts.Select(Map).ToList());
 }

 public async Task<CashierShiftItem> OpenShiftAsync(string locationId,decimal openingCash,CancellationToken ct=default)
 {
  _permissions.Demand("daily_closing.perform");if(openingCash<0)throw new ArgumentOutOfRangeException(nameof(openingCash));var user=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");var date=BusinessDate();
  await using var c=await _factory.CreateDbContextAsync(ct);await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);await RequireLocation(c,locationId,ct);
  if(await c.Set<CashierShiftEntity>().AnyAsync(x=>x.UserId==user&&x.Status=="open",ct))throw new InvalidOperationException("You already have an open cashier shift.");
  if(await SalesBlockedAsync(c,locationId,date,ct))throw new InvalidOperationException("This business day is already finalized.");
  var now=_clock.UtcNow;var row=new CashierShiftEntity{Id=Guid.CreateVersion7().ToString(),StockLocationId=locationId,UserId=user,BusinessDate=date,Status="open",OpeningCash=S(openingCash),OpenedAt=now,CreatedAt=now,UpdatedAt=now};c.Add(row);await c.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Map(row);
 }

 public async Task<CashierShiftItem> CloseShiftAsync(string shiftId,decimal countedCash,string? notes,CancellationToken ct=default)
 {
  _permissions.Demand("daily_closing.perform");if(countedCash<0)throw new ArgumentOutOfRangeException(nameof(countedCash));notes=N(notes,1000);var user=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");
  await using var c=await _factory.CreateDbContextAsync(ct);await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);var row=await c.Set<CashierShiftEntity>().SingleOrDefaultAsync(x=>x.Id==shiftId,ct)??throw new InvalidOperationException("Shift was not found.");
  if(row.Status!="open")throw new InvalidOperationException("This shift is already closed.");if(row.UserId!=user&&!_permissions.HasPermission("daily_closing.approve"))throw new UnauthorizedAccessException();
  var cashRows=await c.Set<SalePaymentEntity>().Where(p=>p.Method=="cash"&&p.PaidAt>=row.OpenedAt&&p.Sale.StockLocationId==row.StockLocationId&&p.Sale.CreatedBy==row.UserId&&p.Sale.BusinessDate==row.BusinessDate&&p.Sale.Status=="completed").Select(p=>p.Amount).ToListAsync(ct);
  var changeRows=await c.Set<SaleEntity>().Where(s=>s.StockLocationId==row.StockLocationId&&s.CreatedBy==row.UserId&&s.BusinessDate==row.BusinessDate&&s.Status=="completed"&&s.CompletedAt>=row.OpenedAt).Select(s=>s.ChangeTotal).ToListAsync(ct);
  var expected=S(row.OpeningCash+cashRows.Sum()-changeRows.Sum());var counted=S(countedCash);row.Status="closed";row.ExpectedCash=expected;row.CountedCash=counted;row.Variance=S(counted-expected);row.ClosedAt=_clock.UtcNow;row.ClosingNotes=notes;row.UpdatedAt=_clock.UtcNow;await c.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Map(row);
 }

 public async Task<DailyClosingItem> FinalizeAsync(string locationId,decimal? countedCash,string? notes,CancellationToken ct=default)
 {
  _permissions.Demand("daily_closing.perform");if(countedCash is null)throw new InvalidOperationException("Counted cash is required.");if(countedCash<0)throw new ArgumentOutOfRangeException(nameof(countedCash));notes=N(notes,2000);var user=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");var date=BusinessDate();
  await _accounts.EnsureDefaultsAsync(ct);await using var c=await _factory.CreateDbContextAsync(ct);await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);await RequireLocation(c,locationId,ct);
  var row=await ClosingQuery(c).SingleOrDefaultAsync(x=>x.StockLocationId==locationId&&x.BusinessDate==date,ct);if(row is not null&&(row.Status=="finalized"||row.Status=="approved"))throw new InvalidOperationException("This business day is already finalized.");
  if(await c.Set<CashierShiftEntity>().AnyAsync(x=>x.StockLocationId==locationId&&x.BusinessDate==date&&x.Status=="open",ct))throw new InvalidOperationException("Close all cashier shifts before Daily Closing.");
  var snap=await Snapshot(c,locationId,date,ct);var counted=S(countedCash.Value);var variance=S(counted-snap.ExpectedCash);if(variance!=0m&&string.IsNullOrWhiteSpace(notes))throw new InvalidOperationException("A note is required for this cash variance.");
  var now=_clock.UtcNow;row??=new DailyClosingEntity{Id=Guid.CreateVersion7().ToString(),StockLocationId=locationId,BusinessDate=date,CreatedAt=now};if(c.Entry(row).State==EntityState.Detached)c.Add(row);
  row.Status="finalized";Apply(row,snap);row.CountedCash=counted;row.Variance=variance;row.FinalizedBy=user;row.FinalizedAt=now;row.ApprovedBy=null;row.ApprovedAt=null;row.ClosingNotes=notes;row.UpdatedAt=now;
  var ev=Event(row,"finalized",user,notes,JsonSerializer.Serialize(snap),now);c.Add(ev);await c.SaveChangesAsync(ct);if(variance!=0m)await PostVariance(c,row,ev,variance,user,ct);await c.SaveChangesAsync(ct);await tx.CommitAsync(ct);
  await using var read=await _factory.CreateDbContextAsync(ct);return Map(await ClosingQuery(read).AsNoTracking().SingleAsync(x=>x.Id==row.Id,ct));
 }

 public async Task<DailyClosingItem> ApproveAsync(string id,CancellationToken ct=default)
 {
  _permissions.Demand("daily_closing.approve");var user=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");await using var c=await _factory.CreateDbContextAsync(ct);await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);var row=await ClosingQuery(c).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new InvalidOperationException("Daily Closing was not found.");if(row.Status!="finalized")throw new InvalidOperationException("Only a finalized Daily Closing can be approved.");var now=_clock.UtcNow;row.Status="approved";row.ApprovedBy=user;row.ApprovedAt=now;row.UpdatedAt=now;c.Add(Event(row,"approved",user,null,null,now));await c.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Map(row);
 }

 public async Task<DailyClosingItem> ReopenAsync(string id,string reason,CancellationToken ct=default)
 {
  _permissions.Demand("daily_closing.approve");reason=N(reason,2000)??throw new ArgumentException("Reason is required.");var user=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");await using var c=await _factory.CreateDbContextAsync(ct);await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);var row=await ClosingQuery(c).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new InvalidOperationException("Daily Closing was not found.");if(row.Status!="finalized"&&row.Status!="approved")throw new InvalidOperationException("This Daily Closing is not finalized.");var now=_clock.UtcNow;row.Status="reopened";row.ReopenedBy=user;row.ReopenedAt=now;row.ReopenReason=reason;row.UpdatedAt=now;c.Add(Event(row,"reopened",user,reason,null,now));
  var journals=await c.Set<JournalEntryEntity>().Include(x=>x.Lines).ThenInclude(x=>x.LedgerAccount).Where(x=>x.SourceType=="daily_closing"&&x.SourceId==row.Id&&x.SourceEvent!=null&&x.SourceEvent.StartsWith("variance:")&&x.Status=="posted").ToListAsync(ct);foreach(var j in journals)await _ledger.ReverseAsync(c,j,$"Daily Closing reopened: {reason}",user,ct);await c.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Map(row);
 }

 public async Task<bool> SalesBlockedAsync(string locationId,DateOnly? date=null,CancellationToken ct=default)
 {await using var c=await _factory.CreateDbContextAsync(ct);return await SalesBlockedAsync(c,locationId,date??BusinessDate(),ct);}

 private async Task<DailyClosingSnapshot> Snapshot(PharmacyDbContext c,string loc,DateOnly date,CancellationToken ct)
 {
  var sales=await c.Set<SaleEntity>().AsNoTracking().Where(x=>x.StockLocationId==loc&&x.BusinessDate==date&&x.Status=="completed").ToListAsync(ct);
  var saleIds=sales.Select(x=>x.Id).ToList();var pays=await c.Set<SalePaymentEntity>().AsNoTracking().Where(x=>saleIds.Contains(x.SaleId)).ToListAsync(ct);
  var returns=await c.Set<SaleReturnEntity>().AsNoTracking().Where(x=>x.StockLocationId==loc&&x.BusinessDate==date&&x.Status=="completed").ToListAsync(ct);var returnIds=returns.Select(x=>x.Id).ToList();var refunds=await c.Set<SaleReturnRefundEntity>().AsNoTracking().Where(x=>returnIds.Contains(x.SaleReturnId)).ToListAsync(ct);
  var shifts=await c.Set<CashierShiftEntity>().AsNoTracking().Where(x=>x.StockLocationId==loc&&x.BusinessDate==date).ToListAsync(ct);
  decimal P(string method)=>S(pays.Where(x=>x.Method==method).Sum(x=>x.Amount));decimal R(string method)=>S(refunds.Where(x=>x.Method==method).Sum(x=>x.Amount));
  var change=S(sales.Sum(x=>x.ChangeTotal));var opening=S(shifts.Sum(x=>x.OpeningCash));var cash=S(P("cash")-change-R("cash"));var credit=S(sales.Sum(x=>x.DueTotal)-R("credit"));
  return new(date,S(sales.Sum(x=>x.GrandTotal)),S(sales.Sum(x=>x.DiscountTotal)),S(returns.Sum(x=>x.RefundTotal)),cash,S(P("bank")-R("bank")),S(P("mobile")-R("mobile")),credit,opening,S(opening+cash));
 }
 private async Task PostVariance(PharmacyDbContext c,DailyClosingEntity closing,DailyClosingEventEntity ev,decimal variance,string user,CancellationToken ct)
 {
  var cash=await c.Set<LedgerAccountEntity>().SingleAsync(x=>x.SystemKey=="cash_on_hand"&&x.IsActive,ct);var over=await c.Set<LedgerAccountEntity>().SingleAsync(x=>x.SystemKey=="cash_over_short"&&x.IsActive,ct);var amount=Math.Abs(variance);
  IReadOnlyList<JournalLineDraft> lines=variance>0?[new JournalLineDraft(cash,closing.StockLocationId,amount,0m,Memo:"Cash over at Daily Closing"),new JournalLineDraft(over,closing.StockLocationId,0m,amount,Memo:"Cash over at Daily Closing")]:[new JournalLineDraft(over,closing.StockLocationId,amount,0m,Memo:"Cash shortage at Daily Closing"),new JournalLineDraft(cash,closing.StockLocationId,0m,amount,Memo:"Cash shortage at Daily Closing")];
  await _ledger.PostAsync(c,new JournalPostDraft(closing.BusinessDate,ev.OccurredAt,"AFN","daily_closing",closing.Id,$"variance:{ev.Id}",closing.BusinessDate.ToString(),$"accounting:daily-closing-variance:{ev.Id}",null,$"Daily Closing cash variance {closing.BusinessDate}",user),lines,ct);
 }
 private static async Task RequireLocation(PharmacyDbContext c,string id,CancellationToken ct){if(!await c.Set<StockLocationEntity>().AnyAsync(x=>x.Id==id&&x.IsActive,ct))throw new InvalidOperationException("Stock location was not found or is inactive.");}
 internal static Task<bool> SalesBlockedAsync(PharmacyDbContext c,string loc,DateOnly d,CancellationToken ct)=>c.Set<DailyClosingEntity>().AnyAsync(x=>x.StockLocationId==loc&&x.BusinessDate==d&&(x.Status=="finalized"||x.Status=="approved"),ct);
 private static IQueryable<DailyClosingEntity> ClosingQuery(PharmacyDbContext c)=>c.Set<DailyClosingEntity>().Include(x=>x.Events);
 private static DailyClosingEventEntity Event(DailyClosingEntity row,string type,string? actor,string? reason,string? snap,DateTimeOffset now)=>new(){Id=Guid.CreateVersion7().ToString(),DailyClosingId=row.Id,EventType=type,ActorId=actor,Reason=reason,SnapshotJson=snap,OccurredAt=now,CreatedAt=now,UpdatedAt=now};
 private static void Apply(DailyClosingEntity r,DailyClosingSnapshot s){r.GrossSales=s.GrossSales;r.DiscountTotal=s.DiscountTotal;r.ReturnsTotal=s.ReturnsTotal;r.CashCollected=s.CashCollected;r.BankCollected=s.BankCollected;r.MobileCollected=s.MobileCollected;r.CreditSales=s.CreditSales;r.OpeningCash=s.OpeningCash;r.ExpectedCash=s.ExpectedCash;}
 private static DailyClosingSnapshot Snap(DailyClosingEntity x)=>new(x.BusinessDate,x.GrossSales,x.DiscountTotal,x.ReturnsTotal,x.CashCollected,x.BankCollected,x.MobileCollected,x.CreditSales,x.OpeningCash,x.ExpectedCash);
 private static CashierShiftItem Map(CashierShiftEntity x)=>new(x.Id,x.StockLocationId,x.UserId,x.BusinessDate,x.Status,x.OpeningCash,x.ExpectedCash,x.CountedCash,x.Variance,x.OpenedAt,x.ClosedAt,x.ClosingNotes);
 private static DailyClosingItem Map(DailyClosingEntity x)=>new(x.Id,x.StockLocationId,x.BusinessDate,x.Status,Snap(x),x.CountedCash,x.Variance,x.FinalizedBy,x.ApprovedBy,x.ReopenedBy,x.FinalizedAt,x.ApprovedAt,x.ReopenedAt,x.ClosingNotes,x.ReopenReason,x.Events.OrderBy(e=>e.OccurredAt).Select(e=>new DailyClosingEventItem(e.Id,e.EventType,e.ActorId,e.Reason,e.SnapshotJson,e.OccurredAt)).ToList());
 private static decimal S(decimal x)=>decimal.Round(x,4,MidpointRounding.AwayFromZero);private static string? N(string? x,int max){var s=string.IsNullOrWhiteSpace(x)?null:x.Trim();if(s?.Length>max)throw new ArgumentOutOfRangeException(nameof(x));return s;}
}
