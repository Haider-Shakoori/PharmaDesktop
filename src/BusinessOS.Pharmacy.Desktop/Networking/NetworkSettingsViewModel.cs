using System.IO;
using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.LocalClient;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Networking;

public sealed partial class NetworkSettingsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly ILocalServerDiscovery _discovery;
    private readonly ILocalServerServiceController _serviceController;

    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private DeploymentMode currentMode;
    [ObservableProperty] private DeploymentMode requestedMode;
    [ObservableProperty] private string serverName = string.Empty;
    [ObservableProperty] private string serverHost = string.Empty;
    [ObservableProperty] private int serverPort = NetworkConfiguration.DefaultServerPort;
    [ObservableProperty] private string serverId = string.Empty;
    [ObservableProperty] private string certificateFingerprint = string.Empty;
    [ObservableProperty] private string terminalId = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string serviceStatus = string.Empty;
    [ObservableProperty] private string connectionStatus = string.Empty;
    [ObservableProperty] private string networkProfileStatus = string.Empty;
    [ObservableProperty] private string firewallStatus = string.Empty;
    [ObservableProperty] private string diagnosticsReport = string.Empty;
    [ObservableProperty] private string pairingCode = string.Empty;
    [ObservableProperty] private string pairingExpiry = string.Empty;
    [ObservableProperty] private RegisteredTerminal? selectedTerminal;
    [ObservableProperty] private string renameTerminalTo = string.Empty;
    [ObservableProperty] private bool isBusy;

    public NetworkSettingsViewModel(
        IServiceProvider services,
        INetworkConfigurationStore configurationStore,
        ILocalServerDiscovery discovery,
        ILocalServerServiceController serviceController)
    {
        _services = services;
        _configurationStore = configurationStore;
        _discovery = discovery;
        _serviceController = serviceController;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreatePairingCodeCommand = new AsyncRelayCommand(CreatePairingCodeAsync, () => !IsBusy && CurrentMode == DeploymentMode.Server);
        RevokeTerminalCommand = new AsyncRelayCommand(RevokeTerminalAsync, () => !IsBusy && SelectedTerminal is not null);
        RenameTerminalCommand = new AsyncRelayCommand(RenameTerminalAsync, () => !IsBusy && SelectedTerminal is not null);
        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => !IsBusy && CurrentMode == DeploymentMode.Server);
        ConfigureFirewallCommand = new AsyncRelayCommand(ConfigureFirewallAsync, () => !IsBusy && CurrentMode == DeploymentMode.Server);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy && CurrentMode == DeploymentMode.Client);
        RediscoverCommand = new AsyncRelayCommand(RediscoverAsync, () => !IsBusy && CurrentMode == DeploymentMode.Client);
        ApplyModeCommand = new AsyncRelayCommand(ApplyModeAsync, () => !IsBusy);
        SaveConnectionCommand = new AsyncRelayCommand(SaveConnectionAsync, () => !IsBusy);
        RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync, () => !IsBusy);
    }

    public ObservableCollection<RegisteredTerminal> Terminals { get; } = new();
    public IReadOnlyList<DeploymentMode> Modes { get; } =
        [DeploymentMode.Standalone, DeploymentMode.Server, DeploymentMode.Client];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CreatePairingCodeCommand { get; }
    public IAsyncRelayCommand RevokeTerminalCommand { get; }
    public IAsyncRelayCommand RenameTerminalCommand { get; }
    public IAsyncRelayCommand StartServerCommand { get; }
    public IAsyncRelayCommand ConfigureFirewallCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand RediscoverCommand { get; }
    public IAsyncRelayCommand ApplyModeCommand { get; }
    public IAsyncRelayCommand SaveConnectionCommand { get; }
    public IAsyncRelayCommand RunDiagnosticsCommand { get; }

    public string Title => T("Network & Terminals", "شبکه و ترمینال‌ها", "شبکه او ترمینلونه");
    public string Subtitle => T("Local pharmacy server, terminals and diagnostics", "سرور محلی دواخانه، ترمینال‌ها و عیب‌یابی", "د درملتون محلي سرور، ترمینلونه او تشخیص");
    public string DeploymentModeLabel => T("Deployment mode", "حالت نصب", "د نصب حالت");
    public string ServerNameLabel => T("Server name", "نام سرور", "د سرور نوم");
    public string ServerAddressLabel => T("Server address", "آدرس سرور", "د سرور پته");
    public string PortLabel => T("Port", "پورت", "پورټ");
    public string ServerIdLabel => T("Server UUID", "شناسه سرور", "د سرور پېژند");
    public string CertificateLabel => T("Certificate fingerprint", "اثر انگشت گواهی", "د سند ګوتنښه");
    public string ServiceLabel => T("Local Server service", "سرویس سرور محلی", "د محلي سرور خدمت");
    public string ConnectionLabel => T("Connection", "اتصال", "اړیکه");
    public string TerminalsLabel => T("Registered terminals", "ترمینال‌های ثبت‌شده", "ثبت شوي ترمینلونه");
    public string PairingLabel => T("New terminal pairing", "اتصال ترمینال جدید", "د نوي ترمینل نښلول");
    public string CreatePairingLabel => T("Create 5-minute pairing code", "ایجاد کد اتصال ۵ دقیقه‌ای", "د ۵ دقیقو نښلون کوډ جوړول");
    public string StartServerLabel => T("Start Server", "راه‌اندازی سرور", "سرور پیل کړئ");
    public string FirewallLabel => T("Configure Private Firewall", "تنظیم فایروال خصوصی", "شخصي فایروال تنظیمول");
    public string TestConnectionLabel => T("Test Connection", "آزمایش اتصال", "اړیکه وازمویئ");
    public string RediscoverLabel => T("Find Server Again", "یافتن دوباره سرور", "سرور بیا ومومئ");
    public string ApplyModeLabel => T("Apply Mode", "اعمال حالت", "حالت پلي کړئ");
    public string SaveConnectionLabel => T("Save Address / Port", "ذخیره آدرس / پورت", "پته / پورټ خوندي کړئ");
    public string RenameLabel => T("Rename", "تغییر نام", "نوم بدلول");
    public string RevokeLabel => T("Revoke Terminal", "لغو ترمینال", "ترمینل لغوه کړئ");
    public string RefreshLabel => T("Refresh", "تازه‌سازی", "تازه کول");
    public string RunDiagnosticsLabel => T("Run Diagnostics", "اجرای عیب‌یابی", "تشخیص وچلوئ");
    public string NetworkProfileLabel => T("Windows network profile", "پروفایل شبکه ویندوز", "د وینډوز شبکې پروفایل");
    public string FirewallStatusLabel => T("Firewall status", "وضعیت فایروال", "د فایروال حالت");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLocalizedProperties();
    }

    public async Task LoadAsync()
    {
        await BusyAsync(async () =>
        {
            var configuration = await _configurationStore.LoadAsync();
            ApplyConfiguration(configuration);

            var service = await _serviceController.GetStatusAsync();
            ServiceStatus = service.Message;

            var profile = await _serviceController.GetNetworkProfileStatusAsync();
            NetworkProfileStatus = profile.Message;

            if (configuration.Mode == DeploymentMode.Server)
            {
                var apiFirewall = await _serviceController
                    .GetPrivateFirewallRuleStatusAsync(configuration.ServerPort);
                var discoveryFirewall = await _serviceController
                    .GetPrivateDiscoveryFirewallRuleStatusAsync(configuration.DiscoveryPort);

                FirewallStatus = string.Join(
                    Environment.NewLine,
                    apiFirewall.Message,
                    discoveryFirewall.Message);
            }
            else
            {
                FirewallStatus = T(
                    "Not applicable in this deployment mode.",
                    "در این حالت نصب قابل تطبیق نیست.",
                    "په دې نصب حالت کې نه پلي کېږي.");
            }

            Terminals.Clear();

            if (configuration.Mode == DeploymentMode.Server)
            {
                var terminalService = _services.GetRequiredService<ILocalTerminalService>();
                foreach (var terminal in await terminalService.ListAsync())
                {
                    Terminals.Add(terminal);
                }

                ConnectionStatus = T(
                    $"{Terminals.Count(x => x.IsActive)} active terminal(s)",
                    $"{Terminals.Count(x => x.IsActive)} ترمینال فعال",
                    $"{Terminals.Count(x => x.IsActive)} فعال ترمینل");
            }
            else if (configuration.Mode == DeploymentMode.Client)
            {
                await TestConnectionCoreAsync();
            }
            else
            {
                ConnectionStatus = T(
                    "Standalone mode — no LAN server required.",
                    "حالت مستقل — سرور شبکه لازم نیست.",
                    "خپلواک حالت — د شبکې سرور ته اړتیا نشته.");
            }

            StatusMessage = T(
                "Network configuration loaded.",
                "تنظیمات شبکه بارگذاری شد.",
                "د شبکې تنظیمات پورته شول.");
        });
    }

    private async Task CreatePairingCodeAsync()
    {
        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var service = _services.GetRequiredService<ILocalTerminalService>();
            var pairing = await service.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));

            PairingCode = pairing.Code;
            PairingExpiry = pairing.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            StatusMessage = T(
                "Give this short-lived code only to the terminal you are pairing.",
                "این کد کوتاه‌مدت را فقط به ترمینالی بدهید که متصل می‌کنید.",
                "دا لنډمهاله کوډ یوازې هغه ترمینل ته ورکړئ چې نښلوئ.");
        });
    }

    private async Task RevokeTerminalAsync()
    {
        if (SelectedTerminal is null)
        {
            return;
        }

        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var service = _services.GetRequiredService<ILocalTerminalService>();
            await service.RevokeAsync(SelectedTerminal.TerminalId);
            StatusMessage = T("Terminal revoked.", "ترمینال لغو شد.", "ترمینل لغوه شو.");
            await LoadServerTerminalsCoreAsync(service);
        });
    }

    private async Task RenameTerminalAsync()
    {
        if (SelectedTerminal is null || string.IsNullOrWhiteSpace(RenameTerminalTo))
        {
            return;
        }

        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var service = _services.GetRequiredService<ILocalTerminalService>();
            await service.RenameAsync(SelectedTerminal.TerminalId, RenameTerminalTo);
            RenameTerminalTo = string.Empty;
            StatusMessage = T("Terminal renamed.", "نام ترمینال تغییر کرد.", "د ترمینل نوم بدل شو.");
            await LoadServerTerminalsCoreAsync(service);
        });
    }

    private async Task StartServerAsync()
    {
        await BusyAsync(async () =>
        {
            EnsureServerMode();
            var status = await _serviceController.StartAsync();
            ServiceStatus = status.Message;
            StatusMessage = status.Message;
        });
    }

    private async Task ConfigureFirewallAsync()
    {
        await BusyAsync(async () =>
        {
            EnsureServerMode();

            var configuration = await _configurationStore.LoadAsync();
            var apiResult = await _serviceController
                .EnsurePrivateFirewallRuleAsync(ServerPort);
            var discoveryResult = await _serviceController
                .EnsurePrivateDiscoveryFirewallRuleAsync(configuration.DiscoveryPort);

            FirewallStatus = string.Join(
                Environment.NewLine,
                apiResult.Message,
                discoveryResult.Message);

            StatusMessage = apiResult.Success && discoveryResult.Success
                ? T(
                    "Private LAN firewall rules are configured for the API and automatic discovery.",
                    "قوانین فایروال شبکه خصوصی برای API و کشف خودکار تنظیم شد.",
                    "د شخصي شبکې فایروال قواعد د API او اتومات موندنې لپاره تنظیم شول.")
                : FirewallStatus;
        });
    }

    private async Task TestConnectionAsync()
    {
        await BusyAsync(TestConnectionCoreAsync);
    }

    private async Task TestConnectionCoreAsync()
    {
        if (CurrentMode != DeploymentMode.Client)
        {
            return;
        }

        var monitor = _services.GetService<ILocalServerConnectionMonitor>();
        var status = monitor is not null
            ? await monitor.CheckNowAsync()
            : await _services
                .GetRequiredService<LanTerminalPairingClient>()
                .TestConnectionAsync();

        ConnectionStatus = status.IsConnected
            ? T(
                $"Main Server connected · {status.Latency?.TotalMilliseconds:0} ms",
                $"سرور اصلی متصل · {status.Latency?.TotalMilliseconds:0} میلی‌ثانیه",
                $"اصلي سرور وصل · {status.Latency?.TotalMilliseconds:0} ms")
            : T(
                $"Pharmacy Server unavailable · {status.Message}",
                $"سرور دواخانه در دسترس نیست · {status.Message}",
                $"د درملتون سرور نشته · {status.Message}");
    }

    private async Task RediscoverAsync()
    {
        await BusyAsync(async () =>
        {
            if (CurrentMode != DeploymentMode.Client)
            {
                return;
            }

            var current = await _configurationStore.LoadAsync();
            var servers = await _discovery.DiscoverAsync(TimeSpan.FromSeconds(3));

            var match = servers.FirstOrDefault(x =>
                string.Equals(x.ServerId, current.ServerId, StringComparison.Ordinal) &&
                string.Equals(
                    NormalizeFingerprint(x.CertificateSha256),
                    NormalizeFingerprint(current.ServerCertificateSha256 ?? string.Empty),
                    StringComparison.Ordinal));

            if (match is null)
            {
                throw new InvalidOperationException(
                    T(
                        "The paired Main Pharmacy Server was not found. Darmaltoon will not trust a different server automatically.",
                        "سرور اصلی جفت‌شده یافت نشد. دارملتون به‌صورت خودکار به سرور دیگری اعتماد نمی‌کند.",
                        "جوړ شوی اصلي سرور ونه موندل شو. درملتون بل سرور په اوتومات ډول نه مني."));
            }

            var updated = current with
            {
                ServerHost = match.HostName,
                ServerPort = match.Port,
            };

            await _configurationStore.SaveAsync(updated);
            ApplyConfiguration(updated);
            StatusMessage = T(
                $"Server rediscovered at {match.HostName}:{match.Port}.",
                $"سرور دوباره در {match.HostName}:{match.Port} یافت شد.",
                $"سرور بیا په {match.HostName}:{match.Port} وموندل شو.");
        });
    }

    private async Task ApplyModeAsync()
    {
        await BusyAsync(async () =>
        {
            var current = await _configurationStore.LoadAsync();

            if (RequestedMode == current.Mode)
            {
                StatusMessage = T("Deployment mode is unchanged.", "حالت نصب تغییری نکرد.", "د نصب حالت بدل نه شو.");
                return;
            }

            var safeOwnershipChange =
                (current.Mode == DeploymentMode.Standalone && RequestedMode == DeploymentMode.Server) ||
                (current.Mode == DeploymentMode.Server && RequestedMode == DeploymentMode.Standalone);

            if (!safeOwnershipChange)
            {
                throw new InvalidOperationException(
                    T(
                        "This mode change would change data ownership. Use the explicit migration/recovery workflow; Darmaltoon will not copy, delete or promote client data automatically.",
                        "این تغییر حالت مالکیت داده را تغییر می‌دهد. از روند مهاجرت/بازیابی استفاده کنید؛ دارملتون داده را خودکار حذف یا ارتقا نمی‌دهد.",
                        "دا بدلون د معلوماتو مالکیت بدلوي. د مهاجرت/بیا رغونې بهیر وکاروئ؛ درملتون معلومات په اوتومات ډول نه ړنګوي او نه یې لوړوي."));
            }

            var updated = current with
            {
                Mode = RequestedMode,
                ServerName = string.IsNullOrWhiteSpace(ServerName)
                    ? current.ServerName
                    : ServerName.Trim(),
                ServerPort = ServerPort,
                DiscoveryEnabled = RequestedMode == DeploymentMode.Server,
                IsConfigured = true,
            };

            await _configurationStore.SaveAsync(updated);
            ApplyConfiguration(updated);

            StatusMessage = T(
                "Mode saved safely. Restart Darmaltoon so service composition can switch modes. Existing pharmacy data was not changed.",
                "حالت با امنیت ذخیره شد. دارملتون را دوباره راه‌اندازی کنید. داده‌های موجود تغییر نکرد.",
                "حالت خوندي شو. درملتون بیا پیل کړئ. موجود معلومات بدل نه شول.");
        });
    }

    private async Task SaveConnectionAsync()
    {
        await BusyAsync(async () =>
        {
            var current = await _configurationStore.LoadAsync();

            if (current.Mode == DeploymentMode.Client)
            {
                var updated = current with
                {
                    ServerHost = ServerHost.Trim(),
                    ServerPort = ServerPort,
                };

                updated.Validate();
                await _configurationStore.SaveAsync(updated);
                ApplyConfiguration(updated);
                await TestConnectionCoreAsync();
                return;
            }

            if (current.Mode == DeploymentMode.Server)
            {
                var updated = current with
                {
                    ServerName = ServerName.Trim(),
                    ServerPort = ServerPort,
                };

                updated.Validate();
                await _configurationStore.SaveAsync(updated);
                ApplyConfiguration(updated);
                StatusMessage = T(
                    "Server settings saved. Restart the Local Server service after changing the port.",
                    "تنظیمات سرور ذخیره شد. پس از تغییر پورت سرویس را دوباره راه‌اندازی کنید.",
                    "د سرور تنظیمات خوندي شول. د پورټ له بدلون وروسته خدمت بیا پیل کړئ.");
            }
        });
    }

    private async Task LoadServerTerminalsCoreAsync(ILocalTerminalService service)
    {
        Terminals.Clear();
        foreach (var terminal in await service.ListAsync())
        {
            Terminals.Add(terminal);
        }

        SelectedTerminal = null;
    }

    private void ApplyConfiguration(NetworkConfiguration configuration)
    {
        CurrentMode = configuration.Mode;
        RequestedMode = configuration.Mode;
        ServerName = configuration.ServerName;
        ServerHost = configuration.ServerHost ?? Environment.MachineName;
        ServerPort = configuration.ServerPort;
        ServerId = configuration.ServerId ?? "—";
        CertificateFingerprint = configuration.ServerCertificateSha256 ?? "—";
        TerminalId = configuration.TerminalId ?? "—";
        NotifyCommandStates();
    }

    private void EnsureServerMode()
    {
        if (CurrentMode != DeploymentMode.Server)
        {
            throw new InvalidOperationException("This action is available only on the Main Pharmacy Server.");
        }
    }


    private async Task RunDiagnosticsAsync()
    {
        await BusyAsync(async () =>
        {
            var configuration = await _configurationStore.LoadAsync();
            var service = await _serviceController.GetStatusAsync();
            var profile = await _serviceController.GetNetworkProfileStatusAsync();
            FirewallConfigurationResult apiFirewall;
            FirewallConfigurationResult discoveryFirewall;

            if (configuration.Mode == DeploymentMode.Server)
            {
                apiFirewall = await _serviceController
                    .GetPrivateFirewallRuleStatusAsync(configuration.ServerPort);
                discoveryFirewall = await _serviceController
                    .GetPrivateDiscoveryFirewallRuleStatusAsync(configuration.DiscoveryPort);
            }
            else
            {
                apiFirewall = new FirewallConfigurationResult(
                    true,
                    "Not applicable; this computer is not the Main Pharmacy Server.");
                discoveryFirewall = apiFirewall;
            }

            var paths = _services.GetRequiredService<IApplicationPaths>();
            paths.EnsureCreated();

            var databaseStatus = configuration.Mode == DeploymentMode.Client
                ? "Authoritative database: Main Pharmacy Server (no local authoritative client database)."
                : File.Exists(paths.DatabasePath)
                    ? $"Authoritative database: available locally ({new FileInfo(paths.DatabasePath).Length:N0} bytes)."
                    : "Authoritative database: not created yet.";

            string diskStatus;
            try
            {
                var root = Path.GetPathRoot(paths.RootDirectory);
                var drive = !string.IsNullOrWhiteSpace(root)
                    ? new DriveInfo(root)
                    : null;

                diskStatus = drive is null || !drive.IsReady
                    ? "Disk space: unavailable."
                    : $"Disk free: {drive.AvailableFreeSpace / 1024d / 1024d / 1024d:N1} GB.";
            }
            catch
            {
                diskStatus = "Disk space: unavailable.";
            }

            string licenseStatus;
            if (configuration.Mode == DeploymentMode.Client)
            {
                licenseStatus =
                    "License: inherited from the paired Main Pharmacy Server; this client does not create a separate trial.";
            }
            else
            {
                var licensing = _services.GetService<ILicenseService>();
                if (licensing is null)
                {
                    licenseStatus = "License: service unavailable.";
                }
                else
                {
                    try
                    {
                        var entitlement = await licensing.GetCachedEntitlementAsync();
                        licenseStatus = entitlement is null
                            ? "License: no valid cached server/standalone entitlement."
                            : $"License: tenant {entitlement.TenantId}; offline lease expires {entitlement.ExpiresAt:yyyy-MM-dd HH:mm zzz}.";
                    }
                    catch (Exception exception)
                    {
                        licenseStatus = $"License: verification error — {exception.Message}";
                    }
                }
            }

            if (configuration.Mode == DeploymentMode.Client)
            {
                await TestConnectionCoreAsync();
            }

            NetworkProfileStatus = profile.Message;
            FirewallStatus = configuration.Mode == DeploymentMode.Server
                ? string.Join(
                    Environment.NewLine,
                    apiFirewall.Message,
                    discoveryFirewall.Message)
                : apiFirewall.Message;

            DiagnosticsReport = string.Join(
                Environment.NewLine,
                $"Deployment Mode: {configuration.Mode}",
                $"Server Service: {service.Message}",
                $"LAN Connection: {ConnectionStatus}",
                $"Network Profile: {profile.Message}",
                $"API Firewall: {apiFirewall.Message}",
                $"Discovery Firewall: {discoveryFirewall.Message}",
                $"Address/Port: {(configuration.ServerHost ?? Environment.MachineName)}:{configuration.ServerPort}",
                databaseStatus,
                diskStatus,
                licenseStatus,
                "Cloud sync and LAN connectivity are independent. A cloud outage must not be treated as a LAN outage.");

            StatusMessage = T(
                "Diagnostics completed.",
                "عیب‌یابی تکمیل شد.",
                "تشخیص بشپړ شو.");
        });
    }

    private async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommandStates();

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
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreatePairingCodeCommand.NotifyCanExecuteChanged();
        RevokeTerminalCommand.NotifyCanExecuteChanged();
        RenameTerminalCommand.NotifyCanExecuteChanged();
        StartServerCommand.NotifyCanExecuteChanged();
        ConfigureFirewallCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        RediscoverCommand.NotifyCanExecuteChanged();
        ApplyModeCommand.NotifyCanExecuteChanged();
        SaveConnectionCommand.NotifyCanExecuteChanged();
        RunDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentModeChanged(DeploymentMode value) => NotifyCommandStates();
    partial void OnSelectedTerminalChanged(RegisteredTerminal? value)
    {
        RenameTerminalTo = value?.Name ?? string.Empty;
        NotifyCommandStates();
    }

    private static string NormalizeFingerprint(string value) =>
        value.Replace(":", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToUpperInvariant();

    private string T(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(DeploymentModeLabel));
        OnPropertyChanged(nameof(ServerNameLabel));
        OnPropertyChanged(nameof(ServerAddressLabel));
        OnPropertyChanged(nameof(PortLabel));
        OnPropertyChanged(nameof(ServerIdLabel));
        OnPropertyChanged(nameof(CertificateLabel));
        OnPropertyChanged(nameof(ServiceLabel));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(TerminalsLabel));
        OnPropertyChanged(nameof(PairingLabel));
        OnPropertyChanged(nameof(CreatePairingLabel));
        OnPropertyChanged(nameof(StartServerLabel));
        OnPropertyChanged(nameof(FirewallLabel));
        OnPropertyChanged(nameof(TestConnectionLabel));
        OnPropertyChanged(nameof(RediscoverLabel));
        OnPropertyChanged(nameof(ApplyModeLabel));
        OnPropertyChanged(nameof(SaveConnectionLabel));
        OnPropertyChanged(nameof(RenameLabel));
        OnPropertyChanged(nameof(RevokeLabel));
        OnPropertyChanged(nameof(RefreshLabel));
        OnPropertyChanged(nameof(RunDiagnosticsLabel));
        OnPropertyChanged(nameof(NetworkProfileLabel));
        OnPropertyChanged(nameof(FirewallStatusLabel));
    }
}
