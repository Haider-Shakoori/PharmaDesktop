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
using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Desktop;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Navigation;
using BusinessOS.Pharmacy.Desktop.Pos;
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
        var mode = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : "dashboard";
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

        object currentPage = dashboard;
        var selectedKey = "dashboard";
        if (mode == "pos")
        {
            var pos = new PosViewModel(
                new FakePosService(),
                permissions,
                clock);
            pos.LoadAsync().GetAwaiter().GetResult();

            // Exercise the real barcode flow twice. Each scan must remain an independent cart line.
            pos.SearchText = FakePosService.PrimaryBarcode;
            pos.SearchCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            pos.SearchText = FakePosService.PrimaryBarcode;
            pos.SearchCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            if (pos.Cart.Count != 2 ||
                pos.Cart.Any(line => line.Quantity != 1m) ||
                pos.Cart.Select(line => line).Distinct().Count() != 2)
            {
                throw new InvalidOperationException(
                    "Repeated barcode scans must create two independent quantity-1 cart lines.");
            }

            pos.SearchText = "Amoxicillin";
            pos.SearchCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            pos.SelectedProduct = pos.SearchResults.FirstOrDefault();
            if (pos.AddToCartCommand.CanExecute(null))
            {
                pos.AddToCartCommand.Execute(null);
            }

            currentPage = pos;
            selectedKey = "pos";
        }

        var app = new App();
        app.InitializeComponent();
        var shell = new ScreenshotShell(dashboard, currentPage, selectedKey, session.Current!);
        var window = new MainWindow(null!)
        {
            DataContext = shell,
            Width = 1600,
            Height = 920,
            MinWidth = 1280,
            MinHeight = 760,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        window.Show();
        window.UpdateLayout();

        const int width = 1600;
        const int height = 920;
        if (window.Content is FrameworkElement rootVisual)
        {
            rootVisual.Measure(new Size(width, height));
            rootVisual.Arrange(new Rect(0, 0, width, height));
            rootVisual.UpdateLayout();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)window.Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output)) encoder.Save(stream);

        window.Close();
        app.Shutdown();
        Console.WriteLine($"Captured real WPF {mode} to {output}");
        Console.WriteLine($"Size: {width}x{height}");
    }

    private sealed class ScreenshotShell
    {
        public ScreenshotShell(
            DashboardViewModel dashboard,
            object currentPage,
            string selectedKey,
            UserSessionSnapshot user)
        {
            Dashboard = dashboard;
            CurrentPage = currentPage;
            UserDisplayName = user.Name;
            UserRoleText = "Administrator";
            SelectedLanguage = UiLanguageCatalog.All[0];
            var navigation = new (string Key, string Label, string Group)[]
            {
                ("dashboard", "Dashboard", "Operations"),
                ("pos", "POS (New Sale)", "Operations"),
                ("medicines", "Medicines", "Stock"),
                ("inventory", "Inventory", "Stock"),
                ("batches", "Batches", "Stock"),
                ("purchases", "Purchases", "Purchasing"),
                ("suppliers", "Suppliers", "Purchasing"),
                ("customers", "Customers", "Operations"),
                ("expenses", "Expenses", "Finance"),
                ("closing", "Daily Closing", "Finance"),
                ("reports", "Reports", "Finance"),
                ("returns", "Returns", "Purchasing"),
                ("users", "Users", "Administration"),
                ("roles", "Roles & Permissions", "Administration"),
                ("backup", "Backup", "Administration"),
                ("updates", "Sync & Updates", "System"),
                ("network", "Network & Terminals", "System"),
                ("settings", "Settings", "Administration"),
            };

            NavigationItems = new ObservableCollection<NavigationItemViewModel>(
                navigation.Select(item => new NavigationItemViewModel(
                    item.Key,
                    item.Label,
                    item.Group,
                    string.Equals(item.Key, selectedKey, StringComparison.OrdinalIgnoreCase))));
        }
        public string ApplicationName => "BusinessOS Pharmacy";
        public string ProductName => "BusinessOS Pharmacy";
        public string ParentBrand => "Darmaltoon Pharmacy";
        public FlowDirection LayoutDirection => FlowDirection.LeftToRight;
        public bool SidebarCollapsed => false;
        public double SidebarWidth => 224d;
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
        public ICommand ToggleSidebarCommand { get; } = new NoOpCommand();
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

    private sealed class FakePosService : IPosService
    {
        public const string PrimaryBarcode = "0123456789012";

        private static readonly PosStockLocationItem Location =
            new("loc-main", "Kabul", "Main Store", true);

        private static readonly PosCustomerItem[] Customers =
        [
            new("cust-1", "Ahmad Noori", "0700000001", 5000m),
            new("cust-2", "Fatima Rahimi", "0700000002", 2500m),
            new("cust-3", "Walk-in Corporate", "0700000003", 0m),
        ];

        private static readonly PosProductSearchItem[] Products =
        [
            new(
                "med-para",
                "PARA-500",
                PrimaryBarcode,
                "Paracetamol",
                "Paracetamol",
                "500 mg",
                "tablet",
                12m,
                20m,
                20m,
                22m,
                false,
                [
                    new("batch-para-1", "PA-1026", 6m, 20m, new DateOnly(2027, 2, 28)),
                    new("batch-para-2", "PA-0627", 6m, 22m, new DateOnly(2027, 6, 30)),
                ]),
            new(
                "med-amox",
                "AMOX-250",
                "0123456789029",
                "Amoxicillin",
                "Amoxicillin",
                "250 mg",
                "capsule",
                8m,
                35m,
                35m,
                35m,
                true,
                [
                    new("batch-amox-1", "AM-0327", 8m, 35m, new DateOnly(2027, 3, 31)),
                ]),
            new(
                "med-ome",
                "OME-20",
                "0123456789036",
                "Omeprazole",
                "Omeprazole",
                "20 mg",
                "capsule",
                15m,
                18m,
                18m,
                18m,
                false,
                [
                    new("batch-ome-1", "OM-0527", 15m, 18m, new DateOnly(2027, 5, 31)),
                ]),
        ];

        public Task<PosReferenceData> GetReferenceDataAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PosReferenceData([Location], Customers));

        public Task<IReadOnlyList<PosProductSearchItem>> SearchProductsAsync(
            PosProductSearchFilter filter,
            CancellationToken cancellationToken = default)
        {
            var query = filter.Query.Trim();
            IReadOnlyList<PosProductSearchItem> result = Products
                .Where(item =>
                    string.Equals(item.Barcode, query, StringComparison.OrdinalIgnoreCase) ||
                    item.MedicineCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.BrandName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (item.GenericName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.Strength?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(filter.Take)
                .ToList();

            return Task.FromResult(result);
        }

        public Task<SaleDetail> CheckoutAsync(
            PosCheckoutRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Screenshot harness does not post sales.");

        public Task<IReadOnlyList<SaleListItem>> SearchSalesAsync(
            SaleSearchFilter filter,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SaleListItem> sales =
            [
                new("sale-1", "POS-20261001-0048", new DateOnly(2026, 10, 1),
                    new DateTimeOffset(2026, 10, 1, 10, 18, 0, TimeSpan.FromHours(4.5)),
                    "Walk-in Customer", "Main Store", "paid", 1250m, 1250m, 0m, 0m),
                new("sale-2", "POS-20261001-0047", new DateOnly(2026, 10, 1),
                    new DateTimeOffset(2026, 10, 1, 9, 45, 0, TimeSpan.FromHours(4.5)),
                    "Ahmad Noori", "Main Store", "paid", 2480m, 2480m, 0m, 0m),
                new("sale-3", "POS-20261001-0046", new DateOnly(2026, 10, 1),
                    new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.FromHours(4.5)),
                    "Fatima Rahimi", "Main Store", "partial", 780m, 500m, 280m, 0m),
            ];
            return Task.FromResult(sales);
        }

        public Task<SaleDetail?> GetSaleAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SaleDetail?>(null);
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
