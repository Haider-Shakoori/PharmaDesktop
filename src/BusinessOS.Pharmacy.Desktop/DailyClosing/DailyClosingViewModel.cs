using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.Pharmacy.Desktop.DailyClosing;

public sealed partial class DailyClosingViewModel : ObservableObject
{
    private readonly IDailyClosingService _service;
    private readonly NotificationService _notifications;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private DailyClosingLocationItem? selectedLocation;
    [ObservableProperty] private DailyClosingWorkspace? workspace;
    [ObservableProperty] private decimal openingCash;
    [ObservableProperty] private decimal countedCash;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string reopenReason = "";

    public DailyClosingViewModel(
        IDailyClosingService service,
        NotificationService notifications)
    {
        _service = service;
        _notifications = notifications;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        OpenShiftCommand = new AsyncRelayCommand(
            OpenShift,
            () => !IsBusy && SelectedLocation is not null && Workspace?.MyOpenShift is null);
        CloseShiftCommand = new AsyncRelayCommand(
            CloseShift,
            () => !IsBusy && Workspace?.MyOpenShift is not null);
        FinalizeCommand = new AsyncRelayCommand(
            Finalize,
            () => !IsBusy &&
                  SelectedLocation is not null &&
                  Workspace?.MyOpenShift is null &&
                  (Workspace?.Closing is null || Workspace.Closing.Status == "reopened"));
        ApproveCommand = new AsyncRelayCommand(
            Approve,
            () => !IsBusy && Workspace?.Closing?.Status == "finalized");
        ReopenCommand = new AsyncRelayCommand(
            Reopen,
            () => !IsBusy &&
                  (Workspace?.Closing?.Status == "finalized" || Workspace?.Closing?.Status == "approved") &&
                  !string.IsNullOrWhiteSpace(ReopenReason));
    }

    public ObservableCollection<DailyClosingLocationItem> Locations { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand OpenShiftCommand { get; }
    public IAsyncRelayCommand CloseShiftCommand { get; }
    public IAsyncRelayCommand FinalizeCommand { get; }
    public IAsyncRelayCommand ApproveCommand { get; }
    public IAsyncRelayCommand ReopenCommand { get; }

    public string Eyebrow => T("OPERATIONS & CASH CONTROL", "عملیات و کنترول نقدی", "عملیات او نغدي کنټرول");
    public string Title => T("Daily Closing", "بستن حساب روزانه", "ورځنی حساب");
    public string Subtitle => T(
        "Reconcile cashier shifts, verify the day's payment mix, and finalize the business date with a complete audit trail.",
        "شیفت‌های صندوق را تطبیق کنید، ترکیب پرداخت روز را بررسی کنید و تاریخ کاری را با سابقه کامل حسابرسی نهایی سازید.",
        "د صندوقدار شفټونه برابر کړئ، د ورځې د تادیاتو ترکیب تایید کړئ او کاري نېټه د بشپړ پلټنیز ریکارډ سره وروستۍ کړئ.");

    public string DayStatusText => Workspace?.Closing?.Status switch
    {
        "finalized" => T("Finalized", "نهایی‌شده", "وروستی شوی"),
        "approved" => T("Approved", "تاییدشده", "تایید شوی"),
        "reopened" => T("Reopened", "دوباره باز", "بېرته پرانیستل شوی"),
        _ => T("Open", "باز", "پرانیستی"),
    };

    public string ShiftSummaryText
    {
        get
        {
            var count = Workspace?.Shifts.Count ?? 0;
            return count == 1
                ? T("1 shift recorded", "۱ شیفت ثبت شده", "۱ شفټ ثبت شوی")
                : T($"{count:N0} shifts recorded", $"{count:N0} شیفت ثبت شده", $"{count:N0} شفټونه ثبت شوي");
        }
    }

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(Eyebrow));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(DayStatusText));
        OnPropertyChanged(nameof(ShiftSummaryText));
    }

    partial void OnSelectedLocationChanged(DailyClosingLocationItem? value)
    {
        if (value is not null)
            _ = Reload();
    }

    partial void OnWorkspaceChanged(DailyClosingWorkspace? value)
    {
        OnPropertyChanged(nameof(DayStatusText));
        OnPropertyChanged(nameof(ShiftSummaryText));
        Notify();
    }

    partial void OnReopenReasonChanged(string value) => Notify();

    public async Task LoadAsync() => await Busy(async () =>
    {
        var referenceData = await _service.GetReferenceDataAsync();

        Locations.Clear();
        foreach (var location in referenceData.Locations)
            Locations.Add(location);

        SelectedLocation ??=
            Locations.FirstOrDefault(x => x.IsDefault) ??
            Locations.FirstOrDefault();

        if (SelectedLocation is not null)
        {
            Workspace = await _service.GetWorkspaceAsync(SelectedLocation.Id);
            CountedCash = Workspace.Snapshot.ExpectedCash;
        }
    });

    private async Task Reload()
    {
        if (SelectedLocation is null)
            return;

        Workspace = await _service.GetWorkspaceAsync(SelectedLocation.Id);
        CountedCash = Workspace.Snapshot.ExpectedCash;
        Notify();
    }

    private async Task OpenShift() => await Busy(async () =>
    {
        await _service.OpenShiftAsync(SelectedLocation!.Id, OpeningCash);
        await Reload();
        StatusMessage = T("Shift opened.", "شیفت باز شد.", "شفټ پرانیستل شو.");
    });

    private async Task CloseShift() => await Busy(async () =>
    {
        await _service.CloseShiftAsync(Workspace!.MyOpenShift!.Id, CountedCash, Notes);
        await Reload();
        StatusMessage = T("Shift closed.", "شیفت بسته شد.", "شفټ وتړل شو.");
    });

    private async Task Finalize() => await Busy(async () =>
    {
        await _service.FinalizeAsync(SelectedLocation!.Id, CountedCash, Notes);
        await Reload();
        StatusMessage = T(
            "Daily Closing finalized.",
            "بستن حساب روزانه نهایی شد.",
            "ورځنی حساب وروستی شو.");
        _notifications.ShowSuccess(StatusMessage);
    });

    private async Task Approve() => await Busy(async () =>
    {
        await _service.ApproveAsync(Workspace!.Closing!.Id);
        await Reload();
        StatusMessage = T(
            "Daily Closing approved.",
            "بستن حساب روزانه تایید شد.",
            "ورځنی حساب تایید شو.");
        _notifications.ShowSuccess(StatusMessage);
    });

    private async Task Reopen() => await Busy(async () =>
    {
        await _service.ReopenAsync(Workspace!.Closing!.Id, ReopenReason);
        ReopenReason = "";
        await Reload();
        StatusMessage = T(
            "Business day reopened. Sales can continue for this date.",
            "روز کاری دوباره باز شد. فروش برای این تاریخ ادامه می‌یابد.",
            "کاري ورځ بېرته پرانیستل شوه. د دې نېټې خرڅلاو دوام کولای شي.");
        _notifications.ShowSuccess(StatusMessage);
    });

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
            _notifications.ShowError(exception.Message);
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
        OpenShiftCommand.NotifyCanExecuteChanged();
        CloseShiftCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
        ApproveCommand.NotifyCanExecuteChanged();
        ReopenCommand.NotifyCanExecuteChanged();
    }

    private string T(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}
