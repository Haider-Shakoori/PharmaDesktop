using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

internal sealed class ExpenseService : IExpenseService
{
    private readonly IDbContextFactory<PharmacyDbContext> _factory; private readonly IPermissionAuthorizer _permissions;
    private readonly IUserSessionService _sessions; private readonly IClock _clock; private readonly AccountingProvisioner _provisioner; private readonly LedgerPostingService _ledger;
    public ExpenseService(IDbContextFactory<PharmacyDbContext> factory,IPermissionAuthorizer permissions,IUserSessionService sessions,IClock clock,AccountingProvisioner provisioner,LedgerPostingService ledger)
    { _factory=factory;_permissions=permissions;_sessions=sessions;_clock=clock;_provisioner=provisioner;_ledger=ledger; }

    public async Task EnsureDefaultsAsync(CancellationToken ct=default){_permissions.Demand("accounting.manage");await _provisioner.EnsureDefaultsAsync(ct);}
    public async Task<ExpenseReferenceData> GetReferenceDataAsync(CancellationToken ct=default)
    {
        _permissions.Demand("accounting.manage"); await _provisioner.EnsureDefaultsAsync(ct);
        await using var c=await _factory.CreateDbContextAsync(ct);
        var accounts=await c.Set<LedgerAccountEntity>().AsNoTracking().Where(x=>x.IsActive).OrderBy(x=>x.Code).ToListAsync(ct);
        var locations=await c.Set<StockLocationEntity>().AsNoTracking().Include(x=>x.Branch).Where(x=>x.IsActive&&x.Branch.IsActive).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Name)
            .Select(x=>new ExpenseLocationItem(x.Id,x.Name,x.Branch.Name,x.IsDefault)).ToListAsync(ct);
        LedgerAccountItem Map(LedgerAccountEntity x)=>new(x.Id,x.Code,x.Name,x.Type,x.NormalBalance,x.SystemKey,x.Currency,x.IsSystem,x.IsActive);
        return new ExpenseReferenceData(accounts.Where(x=>x.Type=="expense").Select(Map).ToList(),accounts.Where(IsPaymentAccount).Select(Map).ToList(),locations);
    }

    public async Task<ExpenseDetail> PostAsync(PostExpenseRequest r,CancellationToken ct=default)
    {
        _permissions.Demand("accounting.manage"); Validate(r); await _provisioner.EnsureDefaultsAsync(ct);
        var actor=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");
        await using var c=await _factory.CreateDbContextAsync(ct); await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);
        var existing=await ExpenseQuery(c).AsNoTracking().SingleOrDefaultAsync(x=>x.IdempotencyKey==r.IdempotencyKey.Trim(),ct);
        if(existing is not null){await tx.CommitAsync(ct);return ToDetail(existing);}
        var expense=await c.Set<LedgerAccountEntity>().SingleOrDefaultAsync(x=>x.Id==r.ExpenseAccountId&&x.IsActive,ct)??throw new InvalidOperationException("Expense account was not found or is inactive.");
        var payment=await c.Set<LedgerAccountEntity>().SingleOrDefaultAsync(x=>x.Id==r.PaymentAccountId&&x.IsActive,ct)??throw new InvalidOperationException("Payment account was not found or is inactive.");
        if(expense.Type!="expense") throw new InvalidOperationException("Select an expense account.");
        if(!IsPaymentAccount(payment)) throw new InvalidOperationException("Select a cash, bank, mobile, hawala, or other settlement asset account.");
        StockLocationEntity? location=null;
        if(!string.IsNullOrWhiteSpace(r.StockLocationId)) location=await c.Set<StockLocationEntity>().SingleOrDefaultAsync(x=>x.Id==r.StockLocationId&&x.IsActive,ct)??throw new InvalidOperationException("Stock location was not found or is inactive.");
        var now=_clock.UtcNow; var amount=Scale(r.Amount); var currency=r.Currency.Trim().ToUpperInvariant();
        var row=new ExpenseEntity
        {
            Id=Guid.CreateVersion7().ToString(),ExpenseNumber=$"EXP-{r.BusinessDate:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            ExpenseAccountId=expense.Id,PaymentAccountId=payment.Id,StockLocationId=location?.Id,BusinessDate=r.BusinessDate,Currency=currency,Amount=amount,
            Payee=Norm(r.Payee,180),Reference=Norm(r.Reference,160),Notes=Norm(r.Notes,2000),Status="posted",IdempotencyKey=r.IdempotencyKey.Trim(),
            CreatedBy=actor,PostedAt=now,CreatedAt=now,UpdatedAt=now
        };
        c.Add(row);
        await _ledger.PostAsync(c,new JournalPostDraft(r.BusinessDate,now,currency,"expense",row.Id,"posted",row.ExpenseNumber,$"accounting:expense:{row.Id}",row.Reference,$"Expense {row.ExpenseNumber}{(row.Payee is null?"":$" · {row.Payee}")}",actor),
            [new JournalLineDraft(expense,row.StockLocationId,amount,0m,Memo:row.Notes??"Operating expense"),new JournalLineDraft(payment,row.StockLocationId,0m,amount,Memo:"Expense settlement")],ct);
        await c.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await using var read=await _factory.CreateDbContextAsync(ct); return ToDetail(await ExpenseQuery(read).AsNoTracking().SingleAsync(x=>x.Id==row.Id,ct));
    }

    public async Task<ExpenseDetail> ReverseAsync(string expenseId,string reason,CancellationToken ct=default)
    {
        _permissions.Demand("accounting.manage"); ArgumentException.ThrowIfNullOrWhiteSpace(expenseId);
        reason=reason?.Trim()??""; if(reason.Length<3||reason.Length>2000) throw new ArgumentOutOfRangeException(nameof(reason));
        var actor=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");
        await using var c=await _factory.CreateDbContextAsync(ct); await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);
        var row=await ExpenseQuery(c).SingleOrDefaultAsync(x=>x.Id==expenseId,ct)??throw new InvalidOperationException("Expense was not found.");
        if(row.Status=="reversed"){await tx.CommitAsync(ct);return ToDetail(row);}
        var journal=await c.Set<JournalEntryEntity>().Include(x=>x.Lines).ThenInclude(x=>x.LedgerAccount).SingleAsync(x=>x.SourceType=="expense"&&x.SourceId==row.Id&&x.SourceEvent=="posted"&&x.Status=="posted",ct);
        await _ledger.ReverseAsync(c,journal,reason,actor,ct);
        row.Status="reversed"; row.ReversedAt=_clock.UtcNow; row.UpdatedAt=_clock.UtcNow; await c.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ToDetail(row);
    }

    public async Task<ExpenseDetail> AmendAsync(string expenseId,PostExpenseRequest r,string reason,CancellationToken ct=default)
    {
        _permissions.Demand("accounting.manage");
        ArgumentException.ThrowIfNullOrWhiteSpace(expenseId);
        Validate(r);
        reason=reason?.Trim()??"";
        if(reason.Length<3||reason.Length>2000) throw new ArgumentOutOfRangeException(nameof(reason));
        await _provisioner.EnsureDefaultsAsync(ct);
        var actor=_sessions.Current?.UserId??throw new InvalidOperationException("A pharmacy user must be signed in.");

        await using var c=await _factory.CreateDbContextAsync(ct);
        await using var tx=await InventoryWriteTransaction.BeginAsync(c,ct);

        var original=await ExpenseQuery(c).SingleOrDefaultAsync(x=>x.Id==expenseId,ct)
            ??throw new InvalidOperationException("Expense was not found.");
        if(original.Status!="posted")
            throw new InvalidOperationException("Only a posted expense can be updated.");

        var expense=await c.Set<LedgerAccountEntity>().SingleOrDefaultAsync(x=>x.Id==r.ExpenseAccountId&&x.IsActive,ct)
            ??throw new InvalidOperationException("Expense account was not found or is inactive.");
        var payment=await c.Set<LedgerAccountEntity>().SingleOrDefaultAsync(x=>x.Id==r.PaymentAccountId&&x.IsActive,ct)
            ??throw new InvalidOperationException("Payment account was not found or is inactive.");
        if(expense.Type!="expense") throw new InvalidOperationException("Select an expense account.");
        if(!IsPaymentAccount(payment)) throw new InvalidOperationException("Select a cash, bank, mobile, hawala, or other settlement asset account.");

        StockLocationEntity? location=null;
        if(!string.IsNullOrWhiteSpace(r.StockLocationId))
            location=await c.Set<StockLocationEntity>().SingleOrDefaultAsync(x=>x.Id==r.StockLocationId&&x.IsActive,ct)
                ??throw new InvalidOperationException("Stock location was not found or is inactive.");

        var journal=await c.Set<JournalEntryEntity>()
            .Include(x=>x.Lines).ThenInclude(x=>x.LedgerAccount)
            .SingleAsync(x=>x.SourceType=="expense"&&x.SourceId==original.Id&&x.SourceEvent=="posted"&&x.Status=="posted",ct);
        await _ledger.ReverseAsync(c,journal,reason,actor,ct);

        var now=_clock.UtcNow;
        original.Status="reversed";
        original.ReversedAt=now;
        original.UpdatedAt=now;

        var amount=Scale(r.Amount);
        var currency=r.Currency.Trim().ToUpperInvariant();
        var replacement=new ExpenseEntity
        {
            Id=Guid.CreateVersion7().ToString(),
            ExpenseNumber=$"EXP-{r.BusinessDate:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            ExpenseAccountId=expense.Id,
            PaymentAccountId=payment.Id,
            StockLocationId=location?.Id,
            BusinessDate=r.BusinessDate,
            Currency=currency,
            Amount=amount,
            Payee=Norm(r.Payee,180),
            Reference=Norm(r.Reference,160),
            Notes=Norm(r.Notes,2000),
            Status="posted",
            IdempotencyKey=r.IdempotencyKey.Trim(),
            CreatedBy=actor,
            PostedAt=now,
            CreatedAt=now,
            UpdatedAt=now
        };
        c.Add(replacement);

        await _ledger.PostAsync(c,new JournalPostDraft(
            r.BusinessDate,now,currency,"expense",replacement.Id,"posted",replacement.ExpenseNumber,
            $"accounting:expense:{replacement.Id}",replacement.Reference,
            $"Expense {replacement.ExpenseNumber}{(replacement.Payee is null?"":$" · {replacement.Payee}")} · amended from {original.ExpenseNumber}",actor),
            [
                new JournalLineDraft(expense,replacement.StockLocationId,amount,0m,Memo:replacement.Notes??"Operating expense"),
                new JournalLineDraft(payment,replacement.StockLocationId,0m,amount,Memo:$"Expense settlement · amended from {original.ExpenseNumber}")
            ],ct);

        await c.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await using var read=await _factory.CreateDbContextAsync(ct);
        return ToDetail(await ExpenseQuery(read).AsNoTracking().SingleAsync(x=>x.Id==replacement.Id,ct));
    }

    public async Task<IReadOnlyList<ExpenseListItem>> SearchAsync(ExpenseSearchFilter f,CancellationToken ct=default)
    {
        _permissions.Demand("accounting.manage"); ArgumentNullException.ThrowIfNull(f); var take=Math.Clamp(f.Take,1,1000); var search=Norm(f.Search,180); var status=Norm(f.Status,24)?.ToLowerInvariant();
        await using var c=await _factory.CreateDbContextAsync(ct); var q=ExpenseQuery(c).AsNoTracking();
        if(f.From is not null) q=q.Where(x=>x.BusinessDate>=f.From); if(f.To is not null) q=q.Where(x=>x.BusinessDate<=f.To); if(status is not null) q=q.Where(x=>x.Status==status);
        if(search is not null){var p=$"%{search}%";q=q.Where(x=>EF.Functions.Like(x.ExpenseNumber,p)||(x.Payee!=null&&EF.Functions.Like(x.Payee,p))||(x.Reference!=null&&EF.Functions.Like(x.Reference,p)));}
        var rows = await q.OrderByDescending(x=>x.BusinessDate).ThenByDescending(x=>x.Id).Take(take).ToListAsync(ct);
        return rows.Select(ToListProjection).ToList();
    }
    public async Task<ExpenseDetail?> GetAsync(string id,CancellationToken ct=default){_permissions.Demand("accounting.manage");await using var c=await _factory.CreateDbContextAsync(ct);var x=await ExpenseQuery(c).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);return x is null?null:ToDetail(x);}
    public async Task<IReadOnlyList<JournalEntryDetail>> GetJournalsAsync(string expenseId,CancellationToken ct=default)
    {
        _permissions.Demand("accounting.manage"); await using var c=await _factory.CreateDbContextAsync(ct);
        var rows=await c.Set<JournalEntryEntity>().AsNoTracking().Include(x=>x.Lines).ThenInclude(x=>x.LedgerAccount).Where(x=>x.SourceType=="expense"&&x.SourceId==expenseId).OrderBy(x=>x.Id).ToListAsync(ct);
        return rows.Select(ToJournal).ToList();
    }

    private static IQueryable<ExpenseEntity> ExpenseQuery(PharmacyDbContext c)=>c.Set<ExpenseEntity>().Include(x=>x.ExpenseAccount).Include(x=>x.PaymentAccount).Include(x=>x.StockLocation);
    private static bool IsPaymentAccount(LedgerAccountEntity x)=>x.Type=="asset"&&x.SystemKey!="accounts_receivable"&&x.SystemKey!="inventory";
    private static ExpenseListItem ToListProjection(ExpenseEntity x)=>new(x.Id,x.ExpenseNumber,x.BusinessDate,x.ExpenseAccount.Name,x.PaymentAccount.Name,x.StockLocation==null?null:x.StockLocation.Name,x.Currency,x.Amount,x.Payee,x.Reference,x.Status,x.PostedAt,x.ReversedAt);
    private static ExpenseDetail ToDetail(ExpenseEntity x)=>new(ToListProjection(x),x.Notes,x.CreatedBy,x.IdempotencyKey);
    private static JournalEntryDetail ToJournal(JournalEntryEntity x)=>new(x.Id,x.JournalNumber,x.BusinessDate,x.OccurredAt,x.Status,x.Currency,x.SourceType,x.SourceId,x.SourceEvent,x.SourceNumber,x.Description,x.TotalDebit,x.TotalCredit,x.ReversalOfId,x.ReversalReason,x.Lines.Select(l=>new JournalLineItem(l.Id,l.LedgerAccount.Code,l.LedgerAccount.Name,l.StockLocationId,l.Debit,l.Credit,l.Memo)).ToList());
    private static decimal Scale(decimal x)=>decimal.Round(x,4,MidpointRounding.AwayFromZero);
    private static string? Norm(string? x,int max){var s=string.IsNullOrWhiteSpace(x)?null:x.Trim();if(s?.Length>max)throw new ArgumentOutOfRangeException(nameof(x));return s;}
    private static void Validate(PostExpenseRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);ArgumentException.ThrowIfNullOrWhiteSpace(r.ExpenseAccountId);ArgumentException.ThrowIfNullOrWhiteSpace(r.PaymentAccountId);ArgumentException.ThrowIfNullOrWhiteSpace(r.Currency);ArgumentException.ThrowIfNullOrWhiteSpace(r.IdempotencyKey);
        if(r.IdempotencyKey.Trim().Length>191)throw new ArgumentOutOfRangeException(nameof(r.IdempotencyKey));if(r.Amount<=0||r.Amount>999_999_999_999m)throw new ArgumentOutOfRangeException(nameof(r.Amount));
        var cur=r.Currency.Trim();if(cur.Length!=3||cur.Any(ch=>!char.IsLetter(ch)))throw new ArgumentException("Currency must be a three-letter code.");
        Norm(r.Payee,180);Norm(r.Reference,160);Norm(r.Notes,2000);
    }
}
