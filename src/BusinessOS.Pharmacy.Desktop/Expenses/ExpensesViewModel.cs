using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace BusinessOS.Pharmacy.Desktop.Expenses;
public sealed partial class ExpensesViewModel:ObservableObject
{
    private readonly IExpenseService _service; private readonly IClock _clock; private UiLanguage _language=UiLanguageCatalog.All[0];
    [ObservableProperty] private bool isBusy; [ObservableProperty] private string statusMessage=""; [ObservableProperty] private LedgerAccountItem? selectedExpenseAccount;
    [ObservableProperty] private LedgerAccountItem? selectedPaymentAccount; [ObservableProperty] private ExpenseLocationItem? selectedLocation; [ObservableProperty] private string businessDateText="";
    [ObservableProperty] private decimal amount; [ObservableProperty] private string payee=""; [ObservableProperty] private string reference=""; [ObservableProperty] private string notes="";
    [ObservableProperty] private string searchText=""; [ObservableProperty] private ExpenseListItem? selectedExpense; [ObservableProperty] private string reversalReason="";
    private string _idempotencyKey=Guid.NewGuid().ToString("N");
    public ExpensesViewModel(IExpenseService service,IClock clock){_service=service;_clock=clock;LoadCommand=new AsyncRelayCommand(LoadAsync,()=>!IsBusy);PostCommand=new AsyncRelayCommand(PostAsync,CanPost);ReverseCommand=new AsyncRelayCommand(ReverseAsync,CanReverse);SearchCommand=new AsyncRelayCommand(SearchAsync,()=>!IsBusy);}
    public IAsyncRelayCommand LoadCommand{get;} public IAsyncRelayCommand PostCommand{get;} public IAsyncRelayCommand ReverseCommand{get;} public IAsyncRelayCommand SearchCommand{get;}
    public ObservableCollection<LedgerAccountItem> ExpenseAccounts{get;}=[]; public ObservableCollection<LedgerAccountItem> PaymentAccounts{get;}=[]; public ObservableCollection<ExpenseLocationItem> Locations{get;}=[]; public ObservableCollection<ExpenseListItem> Expenses{get;}=[];
    public string Title=>T("Expenses & Accounting","مصارف و حسابداری","لګښتونه او حسابداري"); public string PostLabel=>T("Post expense","ثبت مصرف","لګښت ثبت کړئ"); public string ReverseLabel=>T("Reverse selected","معکوس کردن انتخاب","ټاکل شوی معکوس کړئ");
    public void SetLanguage(UiLanguage l){_language=l;OnPropertyChanged(nameof(Title));OnPropertyChanged(nameof(PostLabel));OnPropertyChanged(nameof(ReverseLabel));}
    public async Task LoadAsync()=>await Busy(async()=>{var r=await _service.GetReferenceDataAsync();ExpenseAccounts.Clear();foreach(var x in r.ExpenseAccounts)ExpenseAccounts.Add(x);PaymentAccounts.Clear();foreach(var x in r.PaymentAccounts)PaymentAccounts.Add(x);Locations.Clear();foreach(var x in r.Locations)Locations.Add(x);SelectedExpenseAccount??=ExpenseAccounts.FirstOrDefault(x=>x.SystemKey=="operating_expense")??ExpenseAccounts.FirstOrDefault();SelectedPaymentAccount??=PaymentAccounts.FirstOrDefault(x=>x.SystemKey=="cash_on_hand")??PaymentAccounts.FirstOrDefault();SelectedLocation??=Locations.FirstOrDefault(x=>x.IsDefault);BusinessDateText=StockDate();await SearchCore();StatusMessage=T("Accounting defaults and expenses loaded.","حساب‌ها و مصارف بارگذاری شد.","حسابونه او لګښتونه پورته شول.");});
    partial void OnSelectedExpenseAccountChanged(LedgerAccountItem? value)=>Notify(); partial void OnSelectedPaymentAccountChanged(LedgerAccountItem? value)=>Notify(); partial void OnAmountChanged(decimal value)=>Notify(); partial void OnSelectedExpenseChanged(ExpenseListItem? value)=>Notify(); partial void OnReversalReasonChanged(string value)=>Notify();
    private bool CanPost()=>!IsBusy&&SelectedExpenseAccount is not null&&SelectedPaymentAccount is not null&&Amount>0&&DateOnly.TryParse(BusinessDateText,out _);
    private async Task PostAsync(){if(!CanPost())return;await Busy(async()=>{var d=DateOnly.Parse(BusinessDateText);var x=await _service.PostAsync(new PostExpenseRequest(SelectedExpenseAccount!.Id,SelectedPaymentAccount!.Id,SelectedLocation?.Id,d,"AFN",Amount,Payee,Reference,Notes,_idempotencyKey));_idempotencyKey=Guid.NewGuid().ToString("N");Amount=0;Payee=Reference=Notes="";await SearchCore();StatusMessage=$"{T("Posted","ثبت شد","ثبت شو")} {x.Expense.ExpenseNumber}";});}
    private bool CanReverse()=>!IsBusy&&SelectedExpense is not null&&SelectedExpense.Status=="posted"&&!string.IsNullOrWhiteSpace(ReversalReason)&&ReversalReason.Trim().Length>=3;
    private async Task ReverseAsync(){if(SelectedExpense is null)return;await Busy(async()=>{var x=await _service.ReverseAsync(SelectedExpense.Id,ReversalReason);ReversalReason="";await SearchCore();StatusMessage=$"{T("Reversed","معکوس شد","معکوس شو")} {x.Expense.ExpenseNumber}";});}
    private async Task SearchAsync()=>await Busy(SearchCore); private async Task SearchCore(){var rows=await _service.SearchAsync(new ExpenseSearchFilter(Search:SearchText,Take:250));Expenses.Clear();foreach(var x in rows)Expenses.Add(x);}
    private async Task Busy(Func<Task>a){if(IsBusy)return;IsBusy=true;Notify();try{await a();}catch(Exception e){StatusMessage=e.Message;}finally{IsBusy=false;Notify();}}
    private void Notify(){LoadCommand.NotifyCanExecuteChanged();PostCommand.NotifyCanExecuteChanged();ReverseCommand.NotifyCanExecuteChanged();SearchCommand.NotifyCanExecuteChanged();}
    private string StockDate()=>DateOnly.FromDateTime(_clock.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime).ToString("yyyy-MM-dd");
    private string T(string e,string d,string p)=>_language.Code switch{"fa"=>d,"ps"=>p,_=>e};
}
