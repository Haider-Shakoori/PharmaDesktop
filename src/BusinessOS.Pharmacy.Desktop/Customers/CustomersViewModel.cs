using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.Customers;

public sealed partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerService _customers;
    private readonly IPermissionAuthorizer _permissions;
    private UiLanguage _language = UiLanguageCatalog.All[0];
    private List<CustomerListItem> _matchingCustomers = [];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedActiveFilter = "All";
    [ObservableProperty] private CustomerListItem? selectedCustomer;
    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private int pageSize = 15;
    [ObservableProperty] private int totalItems;

    [ObservableProperty] private int totalCustomers;
    [ObservableProperty] private int activeCustomers;
    [ObservableProperty] private int inactiveCustomers;
    [ObservableProperty] private decimal totalCreditLimit;

    [ObservableProperty] private string customerName = string.Empty;
    [ObservableProperty] private string customerPhone = string.Empty;
    [ObservableProperty] private string customerEmail = string.Empty;
    [ObservableProperty] private decimal creditLimit;
    [ObservableProperty] private bool customerIsActive = true;
    [ObservableProperty] private string customerNotes = string.Empty;
    [ObservableProperty] private bool canManageCustomers;

    public CustomersViewModel(
        ICustomerService customers,
        IPermissionAuthorizer permissions)
    {
        _customers = customers;
        _permissions = permissions;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        NewCommand = new RelayCommand(NewCustomer, () => !IsBusy && CanManageCustomers);
        EditCustomerCommand = new AsyncRelayCommand<CustomerListItem>(
            EditCustomerAsync,
            item => item is not null && !IsBusy && CanManageCustomers);
        CloseEditorCommand = new RelayCommand(CloseEditor);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && CanManageCustomers);
        PreviousPageCommand = new RelayCommand(PreviousPage, () => CurrentPage > 1);
        NextPageCommand = new RelayCommand(NextPage, () => CurrentPage < TotalPages);
        GoToPageCommand = new RelayCommand<int>(
            GoToPage,
            page => page >= 1 && page <= TotalPages);
    }

    public ObservableCollection<CustomerListItem> Customers { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    public IReadOnlyList<string> ActiveFilters { get; } = ["All", "Active", "Inactive"];
    public IReadOnlyList<int> PageSizeOptions { get; } = [10, 15, 25, 50];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IAsyncRelayCommand<CustomerListItem> EditCustomerCommand { get; }
    public IRelayCommand CloseEditorCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand PreviousPageCommand { get; }
    public IRelayCommand NextPageCommand { get; }
    public IRelayCommand<int> GoToPageCommand { get; }

    public string Eyebrow => Translate("Customer & credit control", "کنترل مشتری و اعتبار", "د پېرودونکو او پور کنټرول");
    public string Title => Translate("Customers", "مشتریان", "پېرودونکي");
    public string Subtitle => Translate(
        "Customer records, contact details and per-sale credit limits in one place",
        "اطلاعات مشتری، جزئیات تماس و سقف اعتبار هر فروش در یک بخش",
        "د پېرودونکو معلومات، اړیکې او د هر خرڅلاو د پور حد په یوه برخه کې");

    public string CreditRuleText => Translate(
        "The credit limit caps credit used on a single sale. Outstanding credit is derived from completed sales.",
        "سقف اعتبار، اعتبار یک فروش را محدود می‌کند. بدهی باز از فروش‌های تکمیل‌شده محاسبه می‌شود.",
        "د پور حد د یوه خرڅلاو پور محدودوي. پاتې پور له بشپړو شوو خرڅلاو څخه حسابېږي.");

    public string NewCustomerLabel => Translate("New customer", "مشتری جدید", "نوی پېرودونکی");
    public string EditLabel => Translate("Edit", "ویرایش", "سمون");
    public string SaveCustomerLabel => Translate("Save customer", "ذخیره مشتری", "پېرودونکی خوندي کړئ");
    public string EditorTitle => SelectedCustomer is null
        ? Translate("New customer", "مشتری جدید", "نوی پېرودونکی")
        : Translate("Edit customer", "ویرایش مشتری", "پېرودونکی سمول");

    public string TotalCustomersLabel => Translate("Total customers", "مجموع مشتریان", "ټول پېرودونکي");
    public string ActiveCustomersLabel => Translate("Active customers", "مشتریان فعال", "فعال پېرودونکي");
    public string InactiveCustomersLabel => Translate("Inactive customers", "مشتریان غیرفعال", "غیرفعال پېرودونکي");
    public string TotalCreditLimitLabel => Translate("Total credit limits", "مجموع سقف اعتبار", "د پور ټول حد");
    public string TotalCustomersHint => Translate("All customer records", "تمام رکوردهای مشتری", "د پېرودونکو ټول ریکارډونه");
    public string ActiveCustomersHint => Translate("Available for new sales", "قابل استفاده برای فروش جدید", "د نوو خرڅلاو لپاره فعال");
    public string InactiveCustomersHint => Translate("Currently disabled", "در حال حاضر غیرفعال", "اوس مهال غیرفعال");
    public string TotalCreditLimitHint => Translate("Combined per-sale credit capacity", "مجموع ظرفیت اعتبار هر فروش", "د هر خرڅلاو د پور ګډ ظرفیت");
    public string TotalCreditLimitText => $"؋ {TotalCreditLimit:N2}";

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)Math.Max(1, PageSize)));
    public string PageSummary
    {
        get
        {
            if (TotalItems == 0)
            {
                return Translate("Showing 0 customers", "نمایش ۰ مشتری", "۰ پېرودونکي ښودل کېږي");
            }

            var from = ((CurrentPage - 1) * PageSize) + 1;
            var to = Math.Min(CurrentPage * PageSize, TotalItems);
            return Translate(
                $"Showing {from}-{to} of {TotalItems} customers",
                $"نمایش {from}-{to} از {TotalItems} مشتری",
                $"له {TotalItems} پېرودونکو څخه {from}-{to} ښودل کېږي");
        }
    }

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLocalizedProperties();
    }

    public async Task LoadAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            CanManageCustomers = _permissions.HasPermission("customers.manage");
            await SearchCoreAsync();
            await LoadSummaryAsync();

            StatusMessage = Translate(
                $"{TotalItems} customer records loaded.",
                $"{TotalItems} رکورد مشتری بارگذاری شد.",
                $"{TotalItems} د پېرودونکو ریکارډونه پورته شول.");
        });
    }

    partial void OnSelectedCustomerChanged(CustomerListItem? value)
    {
        OnPropertyChanged(nameof(EditorTitle));
        EditCustomerCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
        RebuildPageNumbers();
    }

    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        RefreshPage();
    }

    partial void OnTotalItemsChanged(int value)
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
        RebuildPageNumbers();
    }

    partial void OnTotalCreditLimitChanged(decimal value) =>
        OnPropertyChanged(nameof(TotalCreditLimitText));

    private async Task SearchAsync() =>
        await ExecuteBusyAsync(SearchCoreAsync);

    private async Task SearchCoreAsync()
    {
        if (!CanManageCustomers)
        {
            _matchingCustomers = [];
            TotalItems = 0;
            RefreshPage();
            return;
        }

        bool? active = SelectedActiveFilter switch
        {
            "Active" => true,
            "Inactive" => false,
            _ => null,
        };

        var selectedId = SelectedCustomer?.Id;
        var result = await _customers.SearchAsync(
            new CustomerSearchFilter(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                active,
                1000));

        _matchingCustomers = result.ToList();
        TotalItems = _matchingCustomers.Count;
        CurrentPage = 1;
        RefreshPage();

        if (selectedId is not null)
        {
            SelectedCustomer = _matchingCustomers.FirstOrDefault(x => x.Id == selectedId);
        }
    }

    private async Task LoadSummaryAsync()
    {
        if (!CanManageCustomers)
        {
            return;
        }

        var summary = await _customers.GetSummaryAsync();
        TotalCustomers = summary.TotalCustomers;
        ActiveCustomers = summary.ActiveCustomers;
        InactiveCustomers = summary.InactiveCustomers;
        TotalCreditLimit = summary.TotalCreditLimit;
    }

    private void NewCustomer()
    {
        SelectedCustomer = null;
        ResetEditor();
        IsEditorOpen = true;
        StatusMessage = Translate("New customer.", "مشتری جدید.", "نوی پېرودونکی.");
    }

    private async Task EditCustomerAsync(CustomerListItem? item)
    {
        if (item is null || IsBusy || !CanManageCustomers)
        {
            return;
        }

        SelectedCustomer = item;
        await LoadEditorAsync(item.Id);
        IsEditorOpen = true;
    }

    private void CloseEditor() => IsEditorOpen = false;

    private void ResetEditor()
    {
        CustomerName = string.Empty;
        CustomerPhone = string.Empty;
        CustomerEmail = string.Empty;
        CreditLimit = 0m;
        CustomerIsActive = true;
        CustomerNotes = string.Empty;
    }

    private async Task LoadEditorAsync(string id)
    {
        try
        {
            var customer = await _customers.GetAsync(id);
            if (customer is null)
            {
                return;
            }

            CustomerName = customer.Name;
            CustomerPhone = customer.Phone ?? string.Empty;
            CustomerEmail = customer.Email ?? string.Empty;
            CreditLimit = customer.CreditLimit;
            CustomerIsActive = customer.IsActive;
            CustomerNotes = customer.Notes ?? string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task SaveAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var request = new SaveCustomerRequest(
                CustomerName,
                CustomerPhone,
                CustomerEmail,
                CreditLimit,
                CustomerIsActive,
                CustomerNotes);

            string id;
            if (SelectedCustomer is null)
            {
                id = await _customers.CreateAsync(request);
            }
            else
            {
                id = SelectedCustomer.Id;
                await _customers.UpdateAsync(id, request);
            }

            await SearchCoreAsync();
            await LoadSummaryAsync();
            SelectCustomerAndPage(id);
            IsEditorOpen = false;

            StatusMessage = Translate(
                "Customer saved.",
                "مشتری ذخیره شد.",
                "پېرودونکی خوندي شو.");
        });
    }

    private void SelectCustomerAndPage(string id)
    {
        var index = _matchingCustomers.FindIndex(x => x.Id == id);
        if (index < 0)
        {
            SelectedCustomer = null;
            return;
        }

        CurrentPage = (index / Math.Max(1, PageSize)) + 1;
        RefreshPage();
        SelectedCustomer = Customers.FirstOrDefault(x => x.Id == id);
    }

    private void PreviousPage()
    {
        if (CurrentPage <= 1)
        {
            return;
        }

        CurrentPage--;
        RefreshPage();
    }

    private void NextPage()
    {
        if (CurrentPage >= TotalPages)
        {
            return;
        }

        CurrentPage++;
        RefreshPage();
    }

    private void GoToPage(int page)
    {
        if (page < 1 || page > TotalPages || page == CurrentPage)
        {
            return;
        }

        CurrentPage = page;
        RefreshPage();
    }

    private void RefreshPage()
    {
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        var skip = Math.Max(0, (CurrentPage - 1) * Math.Max(1, PageSize));

        Customers.Clear();
        foreach (var item in _matchingCustomers
                     .Skip(skip)
                     .Take(Math.Max(1, PageSize)))
        {
            Customers.Add(item);
        }

        OnPropertyChanged(nameof(PageSummary));
        RebuildPageNumbers();
    }

    private void RebuildPageNumbers()
    {
        var total = TotalPages;
        var start = Math.Max(1, CurrentPage - 2);
        var end = Math.Min(total, start + 4);
        start = Math.Max(1, end - 4);

        PageNumbers.Clear();
        for (var page = start; page <= end; page++)
        {
            PageNumbers.Add(page);
        }
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommands();

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
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        NewCommand.NotifyCanExecuteChanged();
        EditCustomerCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(Eyebrow));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CreditRuleText));
        OnPropertyChanged(nameof(NewCustomerLabel));
        OnPropertyChanged(nameof(EditLabel));
        OnPropertyChanged(nameof(SaveCustomerLabel));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(TotalCustomersLabel));
        OnPropertyChanged(nameof(ActiveCustomersLabel));
        OnPropertyChanged(nameof(InactiveCustomersLabel));
        OnPropertyChanged(nameof(TotalCreditLimitLabel));
        OnPropertyChanged(nameof(TotalCustomersHint));
        OnPropertyChanged(nameof(ActiveCustomersHint));
        OnPropertyChanged(nameof(InactiveCustomersHint));
        OnPropertyChanged(nameof(TotalCreditLimitHint));
        OnPropertyChanged(nameof(PageSummary));
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}
