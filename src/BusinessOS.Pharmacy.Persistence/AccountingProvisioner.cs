using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Pharmacy.Persistence;

internal sealed class AccountingProvisioner
{
    private static readonly (string Key,string Code,string Name,string Type,string Normal)[] Defaults =
    [
        ("cash_on_hand","1000","Cash on Hand","asset","debit"),("bank","1010","Bank","asset","debit"),
        ("mobile_money","1020","Mobile Money","asset","debit"),("hawala_clearing","1030","Hawala Clearing","asset","debit"),
        ("other_clearing","1040","Other Settlement Clearing","asset","debit"),("accounts_receivable","1100","Accounts Receivable","asset","debit"),
        ("inventory","1200","Inventory","asset","debit"),("accounts_payable","2000","Accounts Payable","liability","credit"),
        ("sales_tax_payable","2100","Sales Tax Payable","liability","credit"),("owner_equity","3000","Owner Equity","equity","credit"),
        ("owner_drawings","3100","Owner Drawings","equity","debit"),("sales_revenue","4000","Sales Revenue","revenue","credit"),
        ("sales_discounts","4010","Sales Discounts","revenue","debit"),("sales_returns","4020","Sales Returns","revenue","debit"),
        ("cost_of_goods_sold","5000","Cost of Goods Sold","expense","debit"),("inventory_adjustment","5100","Inventory Adjustments","expense","debit"),
        ("operating_expense","6000","Operating Expenses","expense","debit"),("cash_over_short","6100","Cash Over / Short","expense","debit")
    ];
    private readonly IDbContextFactory<PharmacyDbContext> _factory;
    private readonly IClock _clock;
    public AccountingProvisioner(IDbContextFactory<PharmacyDbContext> factory,IClock clock){_factory=factory;_clock=clock;}
    public async Task EnsureDefaultsAsync(CancellationToken ct=default)
    {
        await using var c=await _factory.CreateDbContextAsync(ct);
        var now=_clock.UtcNow;
        foreach(var d in Defaults)
        {
            var row=await c.Set<LedgerAccountEntity>().SingleOrDefaultAsync(x=>x.SystemKey==d.Key,ct);
            if(row is null)
            {
                c.Add(new LedgerAccountEntity{Id=Guid.CreateVersion7().ToString(),Code=d.Code,Name=d.Name,Type=d.Type,NormalBalance=d.Normal,SystemKey=d.Key,Currency="AFN",IsSystem=true,IsActive=true,CreatedAt=now,UpdatedAt=now});
            }
            else
            {
                row.Code=d.Code; row.Name=d.Name; row.Type=d.Type; row.NormalBalance=d.Normal; row.Currency="AFN"; row.IsSystem=true; row.IsActive=true; row.UpdatedAt=now;
            }
        }
        await c.SaveChangesAsync(ct);
    }
}
