using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Expenses;

public sealed partial class ExpensesViewModel : ObservableObject
{
    private readonly IExpenseService _service;
    private readonly IClock _clock;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private string _idempotencyKey = Guid.NewGuid().ToString("N");
    private string? _editingExpenseId;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private LedgerAccountItem? selectedExpenseAccount;
    [ObservableProperty] private LedgerAccountItem? selectedPaymentAccount;
    [ObservableProperty] private ExpenseLocationItem? selectedLocation;
    [ObservableProperty] private string businessDateText = "";
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string payee = "";
    [ObservableProperty] private string reference = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private ExpenseListItem? selectedExpense;
    [ObservableProperty] private string reversalReason = "";
    [ObservableProperty] private bool isEditingExpense;
    [ObservableProperty] private bool isEditorOpen;

    public ExpensesViewModel(IExpenseService service, IClock clock)
    {
        _service = service;
        _clock = clock;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        PostCommand = new AsyncRelayCommand(PostAsync, CanPost);
        ReverseCommand = new AsyncRelayCommand(ReverseAsync, CanReverse);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        NewExpenseCommand = new RelayCommand(NewExpense, () => !IsBusy);
        CancelEditCommand = new RelayCommand(CancelEdit, () => !IsBusy && IsEditorOpen);
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand PostCommand { get; }
    public IAsyncRelayCommand ReverseCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand NewExpenseCommand { get; }
    public IRelayCommand CancelEditCommand { get; }

    public ObservableCollection<LedgerAccountItem> ExpenseAccounts { get; } = [];
    public ObservableCollection<LedgerAccountItem> PaymentAccounts { get; } = [];
    public ObservableCollection<ExpenseLocationItem> Locations { get; } = [];
    public ObservableCollection<ExpenseListItem> Expenses { get; } = [];

    public string Eyebrow => T("FINANCE & CONTROL", "مالی و کنترول", "مالي او کنټرول");
    public string Title => T("Expenses & Accounting", "مصارف و حسابداری", "لګښتونه او حسابداري");
    public string Subtitle => T(
        "Record operating expenses, review payment activity, and keep every correction auditable.",
        "مصارف عملیاتی را ثبت کنید، پرداخت‌ها را مرور کنید و هر اصلاح را قابل حسابرسی نگه دارید.",
        "عملياتي لګښتونه ثبت کړئ، تادیات وڅارئ او هر اصلاح د پلټنې وړ وساتئ.");
    public string NewExpenseLabel => T("New expense", "مصرف جدید", "نوی لګښت");
    public string ExpenseEditorTitle => IsEditingExpense
        ? T("Edit expense", "ویرایش مصرف", "لګښت سم کړئ")
        : T("Record expense", "ثبت مصرف", "لګښت ثبت کړئ");
    public string PostLabel => IsEditingExpense
        ? T("Update expense", "به‌روزرسانی مصرف", "لګښت تازه کړئ")
        : T("Post expense", "ثبت مصرف", "لګښت ثبت کړئ");
    public string ReverseLabel => T("Reverse selected", "معکوس کردن انتخاب", "ټاکل شوی معکوس کړئ");
    public string EditHint => IsEditingExpense
        ? T("Editing selected expense. Saving creates an auditable reversal and corrected replacement.", "مصرف انتخاب‌شده در حال ویرایش است. ذخیره، یک معکوس قابل حسابرسی و سند اصلاح‌شده ایجاد می‌کند.", "ټاکل شوی لګښت سمېږي. خوندي کول د پلټنې وړ معکوس او اصلاح شوی بدیل جوړوي.")
        : T("Double-click a posted expense row to edit it.", "برای ویرایش روی ردیف مصرف ثبت‌شده دوبار کلیک کنید.", "د ثبت شوي لګښت د سمولو لپاره پر قطار دوه ځله کلیک وکړئ.");

    public int VisibleExpenseCount => Expenses.Count;
    public int VisiblePostedCount => Expenses.Count(x => string.Equals(x.Status, "posted", StringComparison.OrdinalIgnoreCase));
    public int VisibleReversedCount => Expenses.Count - VisiblePostedCount;
    public decimal VisiblePostedTotal => Expenses
        .Where(x => string.Equals(x.Status, "posted", StringComparison.OrdinalIgnoreCase))
        .Sum(x => x.Amount);
    public decimal VisibleAverageExpense => VisiblePostedCount == 0
        ? 0
        : VisiblePostedTotal / VisiblePostedCount;
    public string VisiblePostedTotalText => $"؋ {VisiblePostedTotal:N2}";
    public string VisibleExpenseCountText => VisibleExpenseCount.ToString("N0");
    public string VisibleReversedCountText => VisibleReversedCount.ToString("N0");
    public string VisibleAverageExpenseText => $"؋ {VisibleAverageExpense:N2}";

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLabels();
    }

    public async Task LoadAsync() => await Busy(async () =>
    {
        var references = await _service.GetReferenceDataAsync();

        ExpenseAccounts.Clear();
        foreach (var item in references.ExpenseAccounts)
            ExpenseAccounts.Add(item);

        PaymentAccounts.Clear();
        foreach (var item in references.PaymentAccounts)
            PaymentAccounts.Add(item);

        Locations.Clear();
        foreach (var item in references.Locations)
            Locations.Add(item);

        SelectedExpenseAccount ??=
            ExpenseAccounts.FirstOrDefault(x => x.SystemKey == "operating_expense") ??
            ExpenseAccounts.FirstOrDefault();
        SelectedPaymentAccount ??=
            PaymentAccounts.FirstOrDefault(x => x.SystemKey == "cash_on_hand") ??
            PaymentAccounts.FirstOrDefault();
        SelectedLocation ??= Locations.FirstOrDefault(x => x.IsDefault);
        BusinessDateText = StockDate();

        await SearchCore();
        StatusMessage = T(
            "Accounting defaults and expenses loaded.",
            "حساب‌ها و مصارف بارگذاری شد.",
            "حسابونه او لګښتونه پورته شول.");
    });

    public async Task EditSelectedExpenseAsync()
    {
        if (SelectedExpense is null || SelectedExpense.Status != "posted")
            return;

        await Busy(async () =>
        {
            var detail = await _service.GetAsync(SelectedExpense.Id);
            if (detail is null)
                throw new InvalidOperationException(T("Expense was not found.", "مصرف پیدا نشد.", "لګښت ونه موندل شو."));

            _editingExpenseId = detail.Expense.Id;
            IsEditingExpense = true;
            IsEditorOpen = true;
            SelectedExpenseAccount = ExpenseAccounts.FirstOrDefault(x =>
                string.Equals(x.Name, detail.Expense.ExpenseAccountName, StringComparison.OrdinalIgnoreCase));
            SelectedPaymentAccount = PaymentAccounts.FirstOrDefault(x =>
                string.Equals(x.Name, detail.Expense.PaymentAccountName, StringComparison.OrdinalIgnoreCase));
            SelectedLocation = detail.Expense.StockLocationName is null
                ? null
                : Locations.FirstOrDefault(x =>
                    string.Equals(x.Name, detail.Expense.StockLocationName, StringComparison.OrdinalIgnoreCase));
            BusinessDateText = detail.Expense.BusinessDate.ToString("yyyy-MM-dd");
            Amount = detail.Expense.Amount;
            Payee = detail.Expense.Payee ?? string.Empty;
            Reference = detail.Expense.Reference ?? string.Empty;
            Notes = detail.Notes ?? string.Empty;
            StatusMessage = T(
                $"Editing {detail.Expense.ExpenseNumber}.",
                $"ویرایش {detail.Expense.ExpenseNumber}.",
                $"{detail.Expense.ExpenseNumber} سمېږي.");
        });
    }

    partial void OnSelectedExpenseAccountChanged(LedgerAccountItem? value) => Notify();
    partial void OnSelectedPaymentAccountChanged(LedgerAccountItem? value) => Notify();
    partial void OnAmountChanged(decimal value) => Notify();
    partial void OnSelectedExpenseChanged(ExpenseListItem? value) => Notify();
    partial void OnReversalReasonChanged(string value) => Notify();
    partial void OnIsEditingExpenseChanged(bool value)
    {
        RaiseLabels();
        Notify();
    }

    partial void OnIsEditorOpenChanged(bool value) => Notify();

    private bool CanPost() =>
        !IsBusy &&
        SelectedExpenseAccount is not null &&
        SelectedPaymentAccount is not null &&
        Amount > 0 &&
        DateOnly.TryParse(BusinessDateText, out _);

    private async Task PostAsync()
    {
        if (!CanPost())
            return;

        await Busy(async () =>
        {
            var request = new PostExpenseRequest(
                SelectedExpenseAccount!.Id,
                SelectedPaymentAccount!.Id,
                SelectedLocation?.Id,
                DateOnly.Parse(BusinessDateText),
                "AFN",
                Amount,
                Payee,
                Reference,
                Notes,
                _idempotencyKey);

            var wasEditing = IsEditingExpense && _editingExpenseId is not null;
            ExpenseDetail result;
            if (wasEditing)
            {
                result = await _service.AmendAsync(
                    _editingExpenseId!,
                    request,
                    "Expense amended from Darmaltoon desktop.");
            }
            else
            {
                result = await _service.PostAsync(request);
            }

            _idempotencyKey = Guid.NewGuid().ToString("N");
            ResetEditor();
            IsEditorOpen = false;
            await SearchCore();
            SelectedExpense = Expenses.FirstOrDefault(x => x.Id == result.Expense.Id);
            StatusMessage = wasEditing
                ? T($"Updated {result.Expense.ExpenseNumber}", $"به‌روزرسانی شد {result.Expense.ExpenseNumber}", $"تازه شو {result.Expense.ExpenseNumber}")
                : T($"Posted {result.Expense.ExpenseNumber}", $"ثبت شد {result.Expense.ExpenseNumber}", $"ثبت شو {result.Expense.ExpenseNumber}");
        });
    }

    private bool CanReverse() =>
        !IsBusy &&
        SelectedExpense is not null &&
        SelectedExpense.Status == "posted" &&
        !string.IsNullOrWhiteSpace(ReversalReason) &&
        ReversalReason.Trim().Length >= 3;

    private async Task ReverseAsync()
    {
        if (SelectedExpense is null)
            return;

        await Busy(async () =>
        {
            var result = await _service.ReverseAsync(SelectedExpense.Id, ReversalReason);
            ReversalReason = "";
            if (_editingExpenseId == result.Expense.Id)
                ResetEditor();
            await SearchCore();
            StatusMessage = $"{T("Reversed", "معکوس شد", "معکوس شو")} {result.Expense.ExpenseNumber}";
        });
    }

    private void NewExpense()
    {
        if (IsBusy)
            return;

        ResetEditor();
        IsEditorOpen = true;
        StatusMessage = T(
            "Ready to record a new expense.",
            "آماده ثبت مصرف جدید.",
            "د نوي لګښت ثبتولو ته چمتو دی.");
    }

    private void CancelEdit()
    {
        var wasEditing = IsEditingExpense;
        ResetEditor();
        IsEditorOpen = false;
        StatusMessage = wasEditing
            ? T("Expense edit cancelled.", "ویرایش مصرف لغو شد.", "د لګښت سمون لغوه شو.")
            : T("New expense cancelled.", "ثبت مصرف جدید لغو شد.", "د نوي لګښت ثبت لغوه شو.");
    }

    private void ResetEditor()
    {
        _editingExpenseId = null;
        IsEditingExpense = false;
        Amount = 0;
        Payee = "";
        Reference = "";
        Notes = "";
        BusinessDateText = StockDate();
        SelectedExpenseAccount =
            ExpenseAccounts.FirstOrDefault(x => x.SystemKey == "operating_expense") ??
            ExpenseAccounts.FirstOrDefault();
        SelectedPaymentAccount =
            PaymentAccounts.FirstOrDefault(x => x.SystemKey == "cash_on_hand") ??
            PaymentAccounts.FirstOrDefault();
        SelectedLocation = Locations.FirstOrDefault(x => x.IsDefault);
    }

    private async Task SearchAsync() => await Busy(SearchCore);

    private async Task SearchCore()
    {
        var rows = await _service.SearchAsync(new ExpenseSearchFilter(Search: SearchText, Take: 250));
        Expenses.Clear();
        foreach (var item in rows)
            Expenses.Add(item);

        RaiseSummaryProperties();
    }

    private void RaiseSummaryProperties()
    {
        OnPropertyChanged(nameof(VisibleExpenseCount));
        OnPropertyChanged(nameof(VisiblePostedCount));
        OnPropertyChanged(nameof(VisibleReversedCount));
        OnPropertyChanged(nameof(VisiblePostedTotal));
        OnPropertyChanged(nameof(VisibleAverageExpense));
        OnPropertyChanged(nameof(VisiblePostedTotalText));
        OnPropertyChanged(nameof(VisibleExpenseCountText));
        OnPropertyChanged(nameof(VisibleReversedCountText));
        OnPropertyChanged(nameof(VisibleAverageExpenseText));
    }

    private async Task Busy(Func<Task> action)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Notify();
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    private void Notify()
    {
        LoadCommand.NotifyCanExecuteChanged();
        PostCommand.NotifyCanExecuteChanged();
        ReverseCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        NewExpenseCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLabels()
    {
        OnPropertyChanged(nameof(Eyebrow));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(NewExpenseLabel));
        OnPropertyChanged(nameof(ExpenseEditorTitle));
        OnPropertyChanged(nameof(PostLabel));
        OnPropertyChanged(nameof(ReverseLabel));
        OnPropertyChanged(nameof(EditHint));
    }

    private string StockDate() =>
        DateOnly.FromDateTime(
            _clock.UtcNow.ToOffset(TimeSpan.FromMinutes(270)).DateTime)
            .ToString("yyyy-MM-dd");

    private string T(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}
