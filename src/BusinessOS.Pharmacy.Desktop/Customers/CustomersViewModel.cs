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

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedActiveFilter = "All";
    [ObservableProperty] private CustomerListItem? selectedCustomer;

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
        NewCommand = new RelayCommand(NewCustomer, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && CanManageCustomers);
    }

    public ObservableCollection<CustomerListItem> Customers { get; } = new();
    public IReadOnlyList<string> ActiveFilters { get; } = ["All", "Active", "Inactive"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }

    public string Title => Translate("Customers & Credit", "مشتریان و اعتبار", "پېرودونکي او پور");
    public string Subtitle => Translate(
        "Customer records and per-sale credit limits",
        "اطلاعات مشتری و سقف اعتبار هر فروش",
        "د پېرودونکو معلومات او د هر خرڅلاو د پور حد");
    public string CreditRuleText => Translate(
        "The credit limit caps credit used on a single sale. Outstanding credit is derived from completed sales.",
        "سقف اعتبار، اعتبار یک فروش را محدود می‌کند. بدهی باز از فروش‌های تکمیل‌شده محاسبه می‌شود.",
        "د پور حد د یوه خرڅلاو پور محدودوي. پاتې پور له بشپړو شوو خرڅلاو څخه حسابېږي.");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CreditRuleText));
    }

    public async Task LoadAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            CanManageCustomers = _permissions.HasPermission("customers.manage");
            await SearchCoreAsync();
            StatusMessage = Translate(
                $"{Customers.Count} customers loaded.",
                $"{Customers.Count} مشتری بارگذاری شد.",
                $"{Customers.Count} پېرودونکي پورته شول.");
        });
    }

    partial void OnSelectedCustomerChanged(CustomerListItem? value)
    {
        if (value is null)
        {
            return;
        }

        _ = LoadEditorAsync(value.Id);
    }

    private async Task SearchAsync() => await ExecuteBusyAsync(SearchCoreAsync);

    private async Task SearchCoreAsync()
    {
        if (!CanManageCustomers)
        {
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
            new CustomerSearchFilter(SearchText, active, 500));

        Customers.Clear();
        foreach (var item in result)
        {
            Customers.Add(item);
        }

        SelectedCustomer = Customers.FirstOrDefault(x => x.Id == selectedId);
    }

    private void NewCustomer()
    {
        SelectedCustomer = null;
        CustomerName = string.Empty;
        CustomerPhone = string.Empty;
        CustomerEmail = string.Empty;
        CreditLimit = 0m;
        CustomerIsActive = true;
        CustomerNotes = string.Empty;
        StatusMessage = Translate("New customer.", "مشتری جدید.", "نوی پېرودونکی.");
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
            SelectedCustomer = Customers.FirstOrDefault(x => x.Id == id);

            StatusMessage = Translate(
                "Customer saved.",
                "مشتری ذخیره شد.",
                "پېرودونکی خوندي شو.");
        });
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
        SaveCommand.NotifyCanExecuteChanged();
    }

    private string Translate(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}
