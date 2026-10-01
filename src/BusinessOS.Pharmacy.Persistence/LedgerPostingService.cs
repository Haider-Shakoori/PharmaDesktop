using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

internal sealed class LedgerPostingService
{
    private readonly IClock _clock;
    public LedgerPostingService(IClock clock)=>_clock=clock;

    public async Task<JournalEntryEntity> PostAsync(PharmacyDbContext c, JournalPostDraft draft, IReadOnlyList<JournalLineDraft> lines, CancellationToken ct)
    {
        var existing=await c.Set<JournalEntryEntity>().Include(x=>x.Lines).ThenInclude(x=>x.LedgerAccount).SingleOrDefaultAsync(x=>x.IdempotencyKey==draft.IdempotencyKey,ct);
        if(existing is not null) return existing;
        if(lines.Count==0) throw new InvalidOperationException("A journal entry requires at least one line.");
        decimal debits=0,credits=0;
        foreach(var l in lines)
        {
            if(l.Debit<0||l.Credit<0||(l.Debit==0&&l.Credit==0)||(l.Debit!=0&&l.Credit!=0)) throw new InvalidOperationException("Each journal line must contain one non-negative debit or credit.");
            debits+=l.Debit; credits+=l.Credit;
        }
        debits=Scale(debits); credits=Scale(credits);
        if(debits!=credits) throw new InvalidOperationException("Journal entry is not balanced.");
        var now=_clock.UtcNow;
        var entry=new JournalEntryEntity
        {
            Id=Guid.CreateVersion7().ToString(),JournalNumber=$"JE-{draft.BusinessDate:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            BusinessDate=draft.BusinessDate,OccurredAt=draft.OccurredAt,Status="posted",Currency=draft.Currency,SourceType=draft.SourceType,SourceId=draft.SourceId,
            SourceEvent=draft.SourceEvent,SourceNumber=draft.SourceNumber,IdempotencyKey=draft.IdempotencyKey,Reference=draft.Reference,Description=draft.Description,
            TotalDebit=debits,TotalCredit=credits,PostedBy=draft.PostedBy,ReversalOfId=draft.ReversalOfId,ReversalReason=draft.ReversalReason,PostedAt=now,CreatedAt=now,UpdatedAt=now
        };
        foreach(var l in lines) entry.Lines.Add(new JournalLineEntity
        {
            Id=Guid.CreateVersion7().ToString(),JournalEntryId=entry.Id,LedgerAccountId=l.Account.Id,StockLocationId=l.StockLocationId,
            Debit=Scale(l.Debit),Credit=Scale(l.Credit),CounterpartyType=l.CounterpartyType,CounterpartyId=l.CounterpartyId,Memo=l.Memo,CreatedAt=now,UpdatedAt=now
        });
        c.Add(entry); await c.SaveChangesAsync(ct); return entry;
    }

    public async Task<JournalEntryEntity> ReverseAsync(PharmacyDbContext c,JournalEntryEntity original,string reason,string? actorId,CancellationToken ct)
    {
        if(original.Status=="reversed")
            return await c.Set<JournalEntryEntity>().Include(x=>x.Lines).SingleAsync(x=>x.ReversalOfId==original.Id,ct);
        var businessDate=StockLedger.BusinessDate(_clock.UtcNow);
        var reversal=await PostAsync(c,new JournalPostDraft(businessDate,_clock.UtcNow,original.Currency,original.SourceType,original.SourceId,"reversal",original.SourceNumber,$"accounting:reversal:{original.Id}",original.JournalNumber,$"Reversal of {original.JournalNumber}: {reason}",actorId,original.Id,reason),
            original.Lines.Select(x=>new JournalLineDraft(x.LedgerAccount,x.StockLocationId,x.Credit,x.Debit,x.CounterpartyType,x.CounterpartyId,$"Reversal: {x.Memo ?? original.Description}")).ToList(),ct);
        original.Status="reversed"; original.ReversalReason=reason; original.UpdatedAt=_clock.UtcNow; await c.SaveChangesAsync(ct); return reversal;
    }
    private static decimal Scale(decimal v)=>decimal.Round(v,4,MidpointRounding.AwayFromZero);
}

internal sealed record JournalPostDraft(DateOnly BusinessDate,DateTimeOffset OccurredAt,string Currency,string SourceType,string SourceId,string? SourceEvent,string? SourceNumber,string IdempotencyKey,string? Reference,string Description,string? PostedBy,string? ReversalOfId=null,string? ReversalReason=null);
internal sealed record JournalLineDraft(LedgerAccountEntity Account,string? StockLocationId,decimal Debit,decimal Credit,string? CounterpartyType=null,string? CounterpartyId=null,string? Memo=null);
