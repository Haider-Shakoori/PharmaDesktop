using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;
public sealed class ExpenseAccountingTests
{
 [Fact] public async Task Defaults_and_expense_post_create_balanced_journal_and_are_idempotent()
 {
  var root=Temp();try{await using var p=Build(root);await Init(p);var s=p.GetRequiredService<IExpenseService>();var refs=await s.GetReferenceDataAsync();Assert.Contains(refs.ExpenseAccounts,x=>x.SystemKey=="operating_expense");Assert.Contains(refs.PaymentAccounts,x=>x.SystemKey=="cash_on_hand");Assert.Contains(refs.PaymentAccounts,x=>x.SystemKey=="bank");Assert.DoesNotContain(refs.PaymentAccounts,x=>x.SystemKey=="inventory"); 
  var expense=refs.ExpenseAccounts.Single(x=>x.SystemKey=="operating_expense");var cash=refs.PaymentAccounts.Single(x=>x.SystemKey=="cash_on_hand");
  var req=new PostExpenseRequest(expense.Id,cash.Id,null,new DateOnly(2026,9,30),"afn",123.45678m,"Landlord","R-1","Rent","exp-1");
  var x=await s.PostAsync(req);Assert.Equal(123.4568m,x.Expense.Amount);Assert.Equal("AFN",x.Expense.Currency);
  var journals=await s.GetJournalsAsync(x.Expense.Id);var j=Assert.Single(journals);Assert.Equal(j.TotalDebit,j.TotalCredit);Assert.Equal(123.4568m,j.TotalDebit);Assert.Equal(2,j.Lines.Count);
  var replay=await s.PostAsync(req);Assert.Equal(x.Expense.Id,replay.Expense.Id);Assert.Single(await s.GetJournalsAsync(x.Expense.Id));}finally{SqliteConnection.ClearAllPools();Delete(root);}
 }
 [Fact] public async Task Inventory_cannot_be_used_as_expense_payment_account()
 {
  var root=Temp();try{await using var p=Build(root);await Init(p);var s=p.GetRequiredService<IExpenseService>();var refs=await s.GetReferenceDataAsync();var expense=refs.ExpenseAccounts.First();
  await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PostAsync(new PostExpenseRequest(expense.Id,expense.Id,null,new DateOnly(2026,9,30),"AFN",10m,null,null,null,"bad-payment")));}finally{SqliteConnection.ClearAllPools();Delete(root);}
 }
 [Fact] public async Task Reversal_posts_opposite_journal_and_is_idempotent()
 {
  var root=Temp();try{await using var p=Build(root);await Init(p);var s=p.GetRequiredService<IExpenseService>();var refs=await s.GetReferenceDataAsync();var row=await s.PostAsync(new PostExpenseRequest(refs.ExpenseAccounts.Single(x=>x.SystemKey=="operating_expense").Id,refs.PaymentAccounts.Single(x=>x.SystemKey=="cash_on_hand").Id,null,new DateOnly(2026,9,30),"AFN",50m,null,null,null,"reverse-me"));
  var reversed=await s.ReverseAsync(row.Expense.Id,"Entered twice");Assert.Equal("reversed",reversed.Expense.Status);var journals=await s.GetJournalsAsync(row.Expense.Id);Assert.Equal(2,journals.Count);Assert.Equal("reversed",journals[0].Status);Assert.Equal("posted",journals[1].Status);Assert.Equal(journals[0].TotalDebit,journals[1].TotalCredit);Assert.Equal(journals[0].TotalCredit,journals[1].TotalDebit);await s.ReverseAsync(row.Expense.Id,"Entered twice");Assert.Equal(2,(await s.GetJournalsAsync(row.Expense.Id)).Count);}finally{SqliteConnection.ClearAllPools();Delete(root);}
 }
 private static async Task Init(ServiceProvider p){await p.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync("tenant-expenses");}
 private static ServiceProvider Build(string root){var s=new ServiceCollection();s.AddBusinessOSInfrastructure(new ApplicationPaths(root));s.AddSingleton<IPermissionAuthorizer>(new Allow());s.AddSingleton<IUserSessionService>(new Session());s.AddBusinessOSPersistence();return s.BuildServiceProvider(true);}
 private static string Temp()=>Path.Combine(Path.GetTempPath(),"darmaltoon-expense-tests",Guid.NewGuid().ToString("N"));private static void Delete(string r){if(Directory.Exists(r))Directory.Delete(r,true);}
 private sealed class Allow:IPermissionAuthorizer{public bool HasPermission(string p)=>true;public void Demand(string p){}}
 private sealed class Session:IUserSessionService{private static readonly UserSessionSnapshot S=new("user-accounting","tenant-expenses","a","d","Accounting Tester","a@test.local",new HashSet<string>{"admin"},new HashSet<string>{"accounting.manage"},DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(8));public UserSessionSnapshot? Current=>S;public Task<UserSessionSnapshot> LoginAsync(string e,string p,bool a,CancellationToken c=default)=>Task.FromResult(S);public Task<UserSessionSnapshot> RefreshAsync(CancellationToken c=default)=>Task.FromResult(S);public Task LogoutAsync(CancellationToken c=default)=>Task.CompletedTask;}
}
