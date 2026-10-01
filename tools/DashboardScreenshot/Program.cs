using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Navigation;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Domain.Licensing;
using BusinessOS.Pharmacy.Licensing;
using Microsoft.Extensions.DependencyInjection;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("dashboard-real.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var now = new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now);
        var permissions = new AllowAllPermissions();
        var session = new FakeSession(now);
        var network = new NetworkConfiguration
        {
            Mode = DeploymentMode.Server,
            ServerName = "Darmaltoon Main Pharmacy Server",
            TenantId = "tenant-darmaltoon",
            IsConfigured = true,
        };
        var services = new ServiceCollection()
            .AddSingleton<ICloudSyncService>(new FakeSyncService(now))
            .BuildServiceProvider();
        var dashboard = new DashboardViewModel(
            new FakeDashboardQueryService(),
            new FakeNetworkStore(network),
            session,
            permissions,
            clock,
            services,
            new FakeActivationStore(now));
        dashboard.LoadAsync().GetAwaiter().GetResult();

        var app = new App();
        app.InitializeComponent();
        var shell = new ScreenshotShell(dashboard, session.Current!);
        var window = new MainWindow(null!)
        {
            DataContext = shell,
            Width = 1600,
            Height = 920,
            MinWidth = 1180,
            MinHeight = 720,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        window.Show();
        window.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output)) encoder.Save(stream);

        window.Close();
        app.Shutdown();
        Console.WriteLine($"Captured real WPF dashboard to {output}");
        Console.WriteLine($"Size: {width}x{height}");
    }

    private sealed class ScreenshotShell
    {
        public ScreenshotShell(DashboardViewModel dashboard, UserSessionSnapshot user)
        {
            Dashboard = dashboard;
            CurrentPage = dashboard;
            UserDisplayName = user.Name;
            UserRoleText = "Administrator";
            SelectedLanguage = UiLanguageCatalog.All[0];
            NavigationItems =
            [
                new("dashboard", "Dashboard", "⌂", true),
                new("pos", "POS (New Sale)", "▣"),
                new("medicines", "Medicines", "✚"),
                new("inventory", "Inventory", "▤"),
                new("batches", "Batches", "◫"),
                new("purchases", "Purchases", "↓"),
                new("suppliers", "Suppliers", "♜"),
                new("customers", "Customers", "♙"),
                new("expenses", "Expenses", "₳"),
                new("closing", "Daily Closing", "✓"),
                new("reports", "Reports", "▥"),
                new("users", "Users", "♟"),
                new("backup", "Backup", "◫"),
                new("settings", "Settings", "⚙"),
                new("returns", "Returns", "↶"),
                new("roles", "Roles & Permissions", "⚿"),
                new("updates", "Sync & Updates", "⇧"),
                new("network", "Network & Terminals", "⌁"),
            ];
        }
        public string ApplicationName => "BusinessOS Pharmacy";
        public string ProductName => "Darmaltoon";
        public string ParentBrand => "BusinessOS.af";
        public FlowDirection LayoutDirection => FlowDirection.LeftToRight;
        public string GlobalSearchText { get; set; } = string.Empty;
        public DashboardViewModel Dashboard { get; }
        public object CurrentPage { get; }
        public string UserDisplayName { get; }
        public string UserRoleText { get; }
        public string OnlineText => "Online";
        public string LicenseText => "Licensed";
        public string LanStatusText => "Main Server";
        public IReadOnlyList<UiLanguage> Languages => UiLanguageCatalog.All;
        public UiLanguage SelectedLanguage { get; set; }
        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
        public ICommand NavigateCommand { get; } = new NoOpCommand();
        public ICommand GlobalSearchCommand { get; } = new NoOpCommand();
        public ICommand LogoutCommand { get; } = new NoOpCommand();
    }

    private sealed class NoOpCommand : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class AllowAllPermissions : IPermissionAuthorizer
    {
        public bool HasPermission(string permission) => true;
        public void Demand(string permission) { }
    }

    private sealed class FakeSession : IUserSessionService
    {
        public FakeSession(DateTimeOffset now)
        {
            Current = new UserSessionSnapshot(
                "user-admin", "tenant-darmaltoon", "activation-main", "windows-main",
                "Ahmad Khan", "admin@darmaltoon.local",
                new HashSet<string> { "admin" },
                new HashSet<string>
                {
                    "dashboard.view","pos.sell","medicines.manage","inventory.manage","inventory.status",
                    "purchases.manage","customers.manage","accounting.manage","daily_closing.perform",
                    "daily_closing.approve","reports.view","users.manage","settings.manage","returns.manage","roles.manage",
                },
                now.AddHours(-2), now.AddHours(10));
        }
        public UserSessionSnapshot? Current { get; }
        public Task<UserSessionSnapshot> LoginAsync(string email, string password, bool allowOfflineSignIn, CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public Task<UserSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeNetworkStore(NetworkConfiguration configuration) : INetworkConfigurationStore
    {
        public Task<NetworkConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(configuration);
        public Task SaveAsync(NetworkConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeActivationStore(DateTimeOffset now) : IActivationStore
    {
        private readonly ActivationState state = CreateState(now);
        public Task<ActivationState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<ActivationState?>(state);
        public Task SaveAsync(ActivationState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static ActivationState CreateState(DateTimeOffset now)
        {
            var features = new HashSet<string> { "cloud_sync", "automatic_updates", "priority_support", "advanced_reports" };
            var entitlement = new EntitlementSnapshot(
                "tenant-darmaltoon","subscription-pro","license-pro",1,"activation-main","windows-main",
                "pro_plan",SubscriptionState.Active,now.AddDays(-107),now.AddDays(258),features);
            var tenant = new TenantSummary(
                "tenant-darmaltoon","Darmaltoon Pharmacy","darmaltoon",
                "https://pharmacy.businessos.af","Asia/Kabul","AFN","en");
            var plan = new PlanSummary(
                "pro_plan",25,10,30,
                JsonSerializer.SerializeToElement(new
                {
                    cloud_sync = true,
                    automatic_updates = true,
                    priority_support = true,
                    advanced_reports = true,
                }));
            return new ActivationState(
                "preview-lease","windows-main",now,now,entitlement,tenant,plan,"active");
        }
    }

    private sealed class FakeSyncService(DateTimeOffset now) : ICloudSyncService
    {
        public CloudSyncRunResult LastResult { get; } =
            new(CloudSyncRunState.Synced,"Synced 2 min ago",now.AddMinutes(-2),now.AddMinutes(-2));
        public Task<CloudSyncRunResult> SyncOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(LastResult);
    }

    private sealed class FakeDashboardQueryService : ILocalDashboardQueryService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(DashboardQueryOptions options, CancellationToken cancellationToken = default)
        {
            var alerts = new List<DashboardAlertItem>
            {
                new("low_stock","Paracetamol 500mg (Tab)","MED-001",null,"Main Store",15,100,null),
                new("low_stock","Amoxicillin 250mg (Cap)","MED-002",null,"Main Store",8,50,null),
                new("low_stock","Omeprazole 20mg (Cap)","MED-003",null,"Main Store",12,50,null),
                new("low_stock","Cetirizine 10mg (Tab)","MED-004",null,"Main Store",7,30,null),
                new("low_stock","Salbutamol Inhaler","MED-005",null,"Main Store",5,20,null),
                new("near_expiry","Azithromycin 500mg","MED-101","AZ2308","Main Store",22,null,new DateOnly(2026,10,10)),
                new("near_expiry","Metformin 500mg","MED-102","MT2211","Main Store",35,null,new DateOnly(2026,10,25)),
                new("expired","Vitamin D3 1000 IU","MED-103","VD2306","Main Store",3,null,new DateOnly(2026,9,25)),
            };
            var lowStock = new List<DashboardLowStockItem>
            {
                new("Paracetamol 500mg (Tab)",15,100,"Low Stock"),
                new("Amoxicillin 250mg (Cap)",8,50,"Low Stock"),
                new("Omeprazole 20mg (Cap)",12,50,"Low Stock"),
                new("Cetirizine 10mg (Tab)",7,30,"Low Stock"),
                new("Salbutamol Inhaler",5,20,"Low Stock"),
            };
            var expiry = new List<DashboardExpiryItem>
            {
                new("Azithromycin 500mg","AZ2308",new DateOnly(2026,10,10),9,"Expiring"),
                new("Metformin 500mg","MT2211",new DateOnly(2026,10,25),24,"Expiring"),
                new("Amlodipine 5mg","AM2401",new DateOnly(2026,11,2),32,"Expiring"),
                new("Vitamin D3 1000 IU","VD2306",new DateOnly(2026,11,12),42,"Expiring"),
                new("Cough Syrup (100ml)","CS2310",new DateOnly(2026,11,17),47,"Expiring"),
            };
            var tx = new List<DashboardTransactionItem>
            {
                new(new DateTimeOffset(2026,10,1,10,18,0,TimeSpan.FromHours(4.5)),"Sale","INV-2026-0048","Walk-in Customer",3,1250,"Cash","paid"),
                new(new DateTimeOffset(2026,10,1,9,45,0,TimeSpan.FromHours(4.5)),"Sale","INV-2026-0047","Ahmad Noori",5,2480,"Card","paid"),
                new(new DateTimeOffset(2026,10,1,9,12,0,TimeSpan.FromHours(4.5)),"Purchase","PUR-2026-0012","Afghan Med Co.",12,18200,"Supplier invoice","open"),
                new(new DateTimeOffset(2026,10,1,8,30,0,TimeSpan.FromHours(4.5)),"Sale","INV-2026-0046","Walk-in Customer",2,780,"Cash","paid"),
                new(new DateTimeOffset(2026,10,1,8,10,0,TimeSpan.FromHours(4.5)),"Sale","INV-2026-0045","Fatima Rahimi",4,1960,"Cash","paid"),
            };
            var timeline = new List<DashboardSalesPoint>
            {
                new(8,500,2), new(9,2800,4), new(10,3600,5), new(11,2600,4),
                new(12,1700,3), new(13,2200,4), new(14,2900,5), new(15,4100,6),
                new(16,5200,7), new(17,4300,6), new(18,3900,5), new(19,7400,7),
            };
            return Task.FromResult(new DashboardSnapshot(
                options.BusinessDate,
                24580m,18200m,3,12,8,1,48,31450m,31500m,528400m,892,12750m,
                1248,2631,36,652480m,418200m,
                alerts,lowStock,expiry,tx,timeline,
                new DashboardDataAvailability(true,true,true,true,true,true)));
        }
    }
}
