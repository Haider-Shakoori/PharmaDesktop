namespace BusinessOS.Pharmacy.Application.Abstractions.Accounting;

public interface IExpenseService
{
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
    Task<ExpenseReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default);
    Task<ExpenseDetail> PostAsync(PostExpenseRequest request, CancellationToken cancellationToken = default);
    Task<ExpenseDetail> ReverseAsync(string expenseId, string reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpenseListItem>> SearchAsync(ExpenseSearchFilter filter, CancellationToken cancellationToken = default);
    Task<ExpenseDetail?> GetAsync(string expenseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JournalEntryDetail>> GetJournalsAsync(string expenseId, CancellationToken cancellationToken = default);
}

public sealed record LedgerAccountItem(string Id, string Code, string Name, string Type, string NormalBalance, string? SystemKey, string Currency, bool IsSystem, bool IsActive);
public sealed record ExpenseLocationItem(string Id, string Name, string BranchName, bool IsDefault);
public sealed record ExpenseReferenceData(IReadOnlyList<LedgerAccountItem> ExpenseAccounts, IReadOnlyList<LedgerAccountItem> PaymentAccounts, IReadOnlyList<ExpenseLocationItem> Locations);
public sealed record PostExpenseRequest(string ExpenseAccountId, string PaymentAccountId, string? StockLocationId, DateOnly BusinessDate, string Currency, decimal Amount, string? Payee, string? Reference, string? Notes, string IdempotencyKey);
public sealed record ExpenseSearchFilter(string? Search = null, DateOnly? From = null, DateOnly? To = null, string? Status = null, int Take = 250);
public sealed record ExpenseListItem(string Id, string ExpenseNumber, DateOnly BusinessDate, string ExpenseAccountName, string PaymentAccountName, string? StockLocationName, string Currency, decimal Amount, string? Payee, string? Reference, string Status, DateTimeOffset PostedAt, DateTimeOffset? ReversedAt);
public sealed record ExpenseDetail(ExpenseListItem Expense, string? Notes, string CreatedBy, string IdempotencyKey);
public sealed record JournalEntryDetail(string Id, string JournalNumber, DateOnly BusinessDate, DateTimeOffset OccurredAt, string Status, string Currency, string SourceType, string SourceId, string? SourceEvent, string? SourceNumber, string Description, decimal TotalDebit, decimal TotalCredit, string? ReversalOfId, string? ReversalReason, IReadOnlyList<JournalLineItem> Lines);
public sealed record JournalLineItem(string Id, string AccountCode, string AccountName, string? StockLocationId, decimal Debit, decimal Credit, string? Memo);
