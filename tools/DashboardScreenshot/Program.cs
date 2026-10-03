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
using BusinessOS.Pharmacy.Desktop.Authentication;
using BusinessOS.Pharmacy.Desktop.Dashboard;
using BusinessOS.Pharmacy.Desktop.Localization;
using BusinessOS.Pharmacy.Desktop.Navigation;
using BusinessOS.Pharmacy.Desktop.Pos;
using BusinessOS.Pharmacy.Desktop.Printing;
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
        if (mode.StartsWith("dashboard-", StringComparison.OrdinalIgnoreCase))
        {
            dashboard.SelectPeriodCommand.ExecuteAsync(mode["dashboard-".Length..]).GetAwaiter().GetResult();
        }

        object currentPage = dashboard;
        PosViewModel? posViewModel = null;
        var selectedKey = "dashboard";
        if (mode == "password")
        {
            currentPage = new PasswordChangeViewModel(session, services);
            selectedKey = "password";
        }

        if (mode is "settings" or "settings-glass")
        {
            var appearanceStore = new BusinessOS.Pharmacy.Desktop.Appearance.AppearanceSettingsStore(
                Path.Combine(Path.GetTempPath(), "businessos-shot", "appearance.json"));
            appearanceStore.Save(new BusinessOS.Pharmacy.Desktop.Appearance.AppearanceSettings(
                (mode == "settings-glass" ? BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Glass
                                          : BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Classic).ToString()));

            var settingsViewModel = new BusinessOS.Pharmacy.Desktop.Networking.NetworkSettingsViewModel(
                services,
                new FakeNetworkStore(network),
                null!,
                null!,
                new BusinessOS.Pharmacy.Desktop.Profile.UserProfileStore(),
                new BusinessOS.Pharmacy.Desktop.Notifications.NotificationService(),
                new BusinessOS.Pharmacy.Desktop.Printing.ReceiptSettingsStore(),
                new BusinessOS.Pharmacy.Desktop.Pos.PosSettingsStore(),
                appearanceStore);

            settingsViewModel.LoadProfile();
            settingsViewModel.LoadReceiptSettings();
            settingsViewModel.LoadPosSettings();
            settingsViewModel.LoadAppearance();

            currentPage = settingsViewModel;
            selectedKey = "settings";
        }

        if (mode is "medicines" or "medicines-glass")
        {
            var medicines = new BusinessOS.Pharmacy.Desktop.Medicines.MedicinesViewModel(
                new FakeMedicineCatalogService(),
                new FakeMedicineCsvService());
            medicines.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            currentPage = medicines;
            selectedKey = "medicines";
        }

        if (mode is "pos" or "pos-payment" or "pos-glass")
        {
            var pos = new PosViewModel(
                new FakePosService(),
                permissions,
                clock,
                new FakeReceiptPrinter(),
                new BusinessOS.Pharmacy.Desktop.Pos.PosSettingsStore());
            posViewModel = pos;
            pos.LoadAsync().GetAwaiter().GetResult();

            // Exercise both scanner modes: no terminator and the common Enter terminator.
            // One physical scan must add exactly one independent cart line in either case.
            pos.SearchText = FakePosService.PrimaryBarcode;
            Thread.Sleep(350);
            if (pos.Cart.Count != 1)
            {
                throw new InvalidOperationException(
                    "An exact barcode must auto-add even when the scanner sends no Enter terminator.");
            }

            pos.SearchText = FakePosService.PrimaryBarcode;
            pos.SearchCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Thread.Sleep(250);

            if (pos.Cart.Count != 2 ||
                pos.Cart.Any(line => line.Quantity != 1m) ||
                pos.Cart.Select(line => line).Distinct().Count() != 2)
            {
                throw new InvalidOperationException(
                    "Repeated barcode scans must create exactly two independent quantity-1 cart lines.");
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

        if (mode == "pos-top-sellers")
        {
            var pos = new PosViewModel(
                new FakePosService(),
                permissions,
                clock,
                new FakeReceiptPrinter(),
                new PosSettingsStore());
            posViewModel = pos;
            pos.LoadAsync().GetAwaiter().GetResult();

            if (!pos.IsTopSellersVisible || pos.TopSellers.Count == 0)
            {
                throw new InvalidOperationException(
                    "Top sellers must be visible and populated from real service data.");
            }

            var before = pos.Cart.Count;
            pos.QuickAddTopSellerCommand.Execute(pos.TopSellers[0]);
            Thread.Sleep(600);

            if (pos.Cart.Count != before + 1)
            {
                throw new InvalidOperationException(
                    "Quick add on a top seller must add exactly one cart line.");
            }

            currentPage = pos;
            selectedKey = "pos";
        }

        if (mode == "pos-top-sellers-off")
        {
            var settings = new PosSettingsStore();
            settings.Save(new PosSettings(ShowTopSellers: false));

            var pos = new PosViewModel(
                new FakePosService(),
                permissions,
                clock,
                new FakeReceiptPrinter(),
                settings);
            posViewModel = pos;
            pos.LoadAsync().GetAwaiter().GetResult();

            if (pos.IsTopSellersVisible || pos.TopSellers.Count > 0)
            {
                throw new InvalidOperationException(
                    "Disabling the setting in Settings must hide the top sellers strip.");
            }

            settings.Save(new PosSettings(ShowTopSellers: true));

            currentPage = pos;
            selectedKey = "pos";
        }

        var app = new App();
        app.InitializeComponent();

        if (mode.Contains("glass"))
        {
            BusinessOS.Pharmacy.Desktop.Appearance.ThemeManager.Apply(
                BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Glass);
        }

        if (mode == "theme-probe")
        {
            RunThemeProbe();
            app.Shutdown();
            return;
        }

        if (mode is "login" or "login-glass")
        {
            var login = new BusinessOS.Pharmacy.Desktop.Authentication.LoginWindow(
                new BusinessOS.Pharmacy.Desktop.Authentication.LoginViewModel(session))
            {
                Width = 760,
                Height = 860,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
            };

            login.Show();
            login.UpdateLayout();

            var loginContent = (FrameworkElement)login.Content;
            loginContent.Measure(new Size(760, 900));
            loginContent.Arrange(new Rect(0, 0, 760, 900));
            loginContent.UpdateLayout();

            CaptureVisual(loginContent, output, 760, 900);
            login.Close();
            app.Shutdown();
            Console.WriteLine($"Captured real WPF {mode} to {output}");
            return;
        }

        if (mode == "barcode-label")
        {
            var label = BarcodeLabelPrinter.BuildLabel(
                new BarcodeLabelModel(
                    "Darmaltoon Pharmacy",
                    "Paracetamol 500 mg",
                    "MED-0001",
                    "AFN 20.00",
                    true, true, true, true),
                BarcodeLabelPrinter.MillimetersToDips(50),
                BarcodeLabelPrinter.MillimetersToDips(30));

            label.Measure(new Size(label.Width, label.Height));
            label.Arrange(new Rect(0, 0, label.Width, label.Height));
            label.UpdateLayout();
            CaptureVisual(label, output, (int)label.Width, (int)label.Height);
            app.Shutdown();
            Console.WriteLine($"Captured real WPF {mode} to {output}");
            return;
        }

        if (mode == "receipt")
        {
            var receiptSale = new SaleDetail(
                new SaleListItem(
                    "sale-1", "POS-20261002-E1F9BBBFB0", new DateOnly(2026, 10, 2),
                    new DateTimeOffset(2026, 10, 2, 10, 18, 0, TimeSpan.FromHours(4.5)),
                    "Walk-in Customer", "Main Stock", "paid", 75m, 75m, 0m, 0m),
                "AFN", 75m, 0m, 0m, "Haider Shakoori", null, null, null, null, null,
                [
                    new SaleLineItem(
                        "l1", "med-para", "Paracetamol 500 mg", "tablet", 1m, 20m, 0m, 0m, 20m, 12m, false,
                        [new SaleBatchAllocationItem("b1", "batch-para-1", "PA-1026", new DateOnly(2027, 2, 28), 1m, 12m, 20m, 20m)]),
                    new SaleLineItem(
                        "l2", "med-amox", "Amoxicillin 250 mg", "capsule", 1m, 35m, 0m, 0m, 35m, 8m, true,
                        [new SaleBatchAllocationItem("b2", "batch-amox-1", "AM-0327", new DateOnly(2027, 3, 31), 1m, 8m, 35m, 35m)]),
                ],
                [
                    new SalePaymentItem(
                        "p1", "PAY-1", "cash", 75m, "AFN", null,
                        new DateTimeOffset(2026, 10, 2, 10, 18, 0, TimeSpan.FromHours(4.5))),
                ]);

            var receipt = new ReceiptPreviewWindow(receiptSale, new SaleReceiptPrinter())
            {
                Width = 920,
                Height = 780,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
            };

            receipt.Show();
            receipt.UpdateLayout();
            CaptureVisual((FrameworkElement)receipt.Content, output);
            receipt.Close();
            app.Shutdown();
            Console.WriteLine($"Captured real WPF {mode} to {output}");
            return;
        }

        if (mode == "pos-payment")
        {
            if (posViewModel is null)
            {
                throw new InvalidOperationException("POS payment screenshot requires a POS view model.");
            }

            posViewModel.PrintInvoiceAfterPayment = true;
            var payment = new PaymentWindow(posViewModel)
            {
                Width = 720,
                Height = 760,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
            };

            payment.Show();
            payment.UpdateLayout();
            CaptureVisual((FrameworkElement)payment.Content, output);
            payment.Close();
            app.Shutdown();
            Console.WriteLine($"Captured real WPF {mode} to {output}");
            return;
        }

        var captureWidth = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1920;
        var captureHeight = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1080;
        var shell = new ScreenshotShell(dashboard, currentPage, selectedKey, session.Current!);
        var window = new MainWindow(null!)
        {
            DataContext = shell,
            Width = captureWidth,
            Height = captureHeight,
            MinWidth = captureWidth,
            MinHeight = captureHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        if (window.Content is not FrameworkElement rootVisual)
        {
            throw new InvalidOperationException("Screenshot shell content was not available.");
        }

        // Lay out the actual shell visual independently of the monitor's HWND size limit.
        // Preserve the window resource scope and data context for all real templates.
        rootVisual.Resources.MergedDictionaries.Add(window.Resources);
        rootVisual.DataContext = shell;
        window.Content = null;
        rootVisual.Width = captureWidth;
        rootVisual.Height = captureHeight;
        rootVisual.Measure(new Size(captureWidth, captureHeight));
        rootVisual.Arrange(new Rect(0, 0, captureWidth, captureHeight));
        rootVisual.UpdateLayout();
        var size = CaptureVisual(rootVisual, output, captureWidth, captureHeight);

        window.Close();
        app.Shutdown();
        Console.WriteLine($"Captured real WPF {mode} to {output}");
        Console.WriteLine($"Size: {size.Width}x{size.Height}");
    }

    private static void RunThemeProbe()
    {
        var app = System.Windows.Application.Current;

        void Assert(bool condition, string label)
        {
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {label}");
            if (!condition)
            {
                throw new InvalidOperationException($"Theme probe failed: {label}");
            }
        }

        BusinessOS.Pharmacy.Desktop.Appearance.ThemeManager.Apply(
            BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Classic);

        Assert(BusinessOS.Pharmacy.Desktop.Appearance.ThemeManager.Current ==
               BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Classic,
            "classic: ThemeManager.Current == Classic");
        Assert(app.TryFindResource("AppBackgroundBrush") is System.Windows.Media.SolidColorBrush,
            "classic: AppBackgroundBrush is solid");
        Assert(app.TryFindResource("CardShadowEffect") is null,
            "classic: no CardShadowEffect");

        BusinessOS.Pharmacy.Desktop.Appearance.ThemeManager.Apply(
            BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Glass);

        Assert(BusinessOS.Pharmacy.Desktop.Appearance.ThemeManager.Current ==
               BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Glass,
            "glass: ThemeManager.Current == Glass");
        Assert(app.TryFindResource("AppBackgroundBrush") is System.Windows.Media.LinearGradientBrush,
            "glass: AppBackgroundBrush is gradient");
        Assert(app.TryFindResource("CardShadowEffect") is System.Windows.Media.Effects.DropShadowEffect,
            "glass: CardShadowEffect present");
        Assert(app.TryFindResource("CardCornerRadius") is System.Windows.CornerRadius { TopLeft: 18 },
            "glass: CardCornerRadius == 18");
        Assert(app.TryFindResource("SidebarItemSelectedBrush") is System.Windows.Media.SolidColorBrush,
            "glass: sidebar selection brush present");

        BusinessOS.Pharmacy.Desktop.Appearance.ThemeManager.Apply(
            BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Classic);

        Assert(app.TryFindResource("AppBackgroundBrush") is System.Windows.Media.SolidColorBrush,
            "classic(return): AppBackgroundBrush is solid again");
        Assert(app.TryFindResource("CardShadowEffect") is null,
            "classic(return): CardShadowEffect removed again");

        var probeFile = Path.Combine(Path.GetTempPath(), "businessos-theme-probe", "appearance.json");
        if (File.Exists(probeFile))
        {
            File.Delete(probeFile);
        }

        var store = new BusinessOS.Pharmacy.Desktop.Appearance.AppearanceSettingsStore(probeFile);
        store.Save(new BusinessOS.Pharmacy.Desktop.Appearance.AppearanceSettings("Glass"));
        var reloaded = new BusinessOS.Pharmacy.Desktop.Appearance.AppearanceSettingsStore(probeFile).Load();
        Assert(reloaded.Theme == "Glass", "persistence: Glass roundtrip");
        store.Save(new BusinessOS.Pharmacy.Desktop.Appearance.AppearanceSettings("Classic"));
        reloaded = new BusinessOS.Pharmacy.Desktop.Appearance.AppearanceSettingsStore(probeFile).Load();
        Assert(reloaded.Theme == "Classic", "persistence: Classic roundtrip");

        Console.WriteLine("Theme probe finished successfully.");
    }

    private static (int Width, int Height) CaptureVisual(
        FrameworkElement rootVisual,
        string output, int? captureWidth = null, int? captureHeight = null)
    {
        rootVisual.UpdateLayout();
        var width = captureWidth ?? Math.Max(1, (int)Math.Ceiling(rootVisual.ActualWidth));
        var height = captureHeight ?? Math.Max(1, (int)Math.Ceiling(rootVisual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(rootVisual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output))
        {
            encoder.Save(stream);
        }

        return (width, height);
    }

    private sealed class FakeReceiptPrinter : ISaleReceiptPrinter
    {
        public bool Print(SaleDetail sale) => true;
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
            IsPosMode = string.Equals(selectedKey, "pos", StringComparison.OrdinalIgnoreCase);
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
                ("barcode", "Barcode Printing", "Stock"),
                ("purchases", "Purchases", "Purchasing"),
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
                ("password", "Change Password", "Administration"),
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
        public bool IsPosMode { get; }
        public bool IsGlassTheme =>
            BusinessOS.Pharmacy.Desktop.Appearance.ThemeManager.Current ==
            BusinessOS.Pharmacy.Desktop.Appearance.AppearanceTheme.Glass;
        public bool IsClassicTheme => !IsGlassTheme;
        public double SidebarWidth => IsPosMode ? 0d : 224d;
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
        public event Action<CloudSyncRunResult>? ResultUpdated { add { } remove { } }
        public Task<CloudSyncRunResult> SyncOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(LastResult);
        public Task<CloudSyncConflictReview> GetConflictReviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CloudSyncConflictReview.Unavailable("Cloud synchronization is disabled for this terminal mode."));
        public Task<CloudSyncConflictReview> RetryConflictAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            GetConflictReviewAsync(cancellationToken);
        public Task<CloudSyncConflictReview> DismissConflictAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            GetConflictReviewAsync(cancellationToken);
    }

    private sealed class FakeMedicineCatalogService : BusinessOS.Pharmacy.Application.Abstractions.Medicines.IMedicineCatalogService
    {
        private static readonly BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineListItem[] Items =
        [
            new("med-para", "PARA-500", "Paracetamol", "Paracetamol", "500 mg", "Tablet", "Analgesics", "Pharma Co", "box", "tablet", 100m, 30m, true, true, true),
            new("med-amox", "AMOX-250", "Amoxicillin", "Amoxicillin", "250 mg", "Capsule", "Antibiotics", "Pharma Co", "box", "capsule", 100m, 15m, true, true, true),
            new("med-ome", "OME-020", "Omeprazole", "Omeprazole", "20 mg", "Capsule", "Gastro", "Medico", "box", "capsule", 60m, 12m, true, true, true),
            new("med-vitd", "VIT-D3", "Vitamin D3", "Cholecalciferol", "50000 IU", "Softgel", "Vitamins", "Medico", "box", "softgel", 50m, 20m, true, true, true),
            new("med-ctz", "CTZ-010", "Cetirizine", "Cetirizine", "10 mg", "Tablet", "Antihistamines", "Global Labs", "box", "tablet", 100m, 20m, false, true, true),
            new("med-met", "MET-850", "Metformin", "Metformin", "850 mg", "Tablet", "Diabetes", "Global Labs", "box", "tablet", 100m, 25m, false, true, true),
        ];

        public Task<IReadOnlyList<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineListItem>> SearchAsync(
            BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineSearchFilter filter,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineListItem> query = Items;

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search.Trim();
                query = query.Where(x =>
                    x.BrandName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    x.MedicineCode.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            if (filter.IsActive is bool active)
            {
                query = query.Where(x => x.IsActive == active);
            }

            return Task.FromResult<IReadOnlyList<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineListItem>>(
                query.Take(filter.Take).ToList());
        }

        public Task<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineEditorModel?> GetAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineEditorModel?>(null);

        public Task<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineReferenceData> GetReferenceDataAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineReferenceData(
                [
                    new("cat-1", "Analgesics", true),
                    new("cat-2", "Antibiotics", true),
                    new("cat-3", "Vitamins", true),
                ],
                [
                    new("man-1", "Pharma Co", "Afghanistan", true),
                    new("man-2", "Medico", "Pakistan", true),
                ]));

        public Task<string> CreateAsync(
            BusinessOS.Pharmacy.Application.Abstractions.Medicines.SaveMedicineRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult("med-new");

        public Task UpdateAsync(
            string id,
            BusinessOS.Pharmacy.Application.Abstractions.Medicines.SaveMedicineRequest request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult("cat-new");

        public Task<string> CreateManufacturerAsync(
            string name,
            string? country,
            CancellationToken cancellationToken = default) => Task.FromResult("man-new");
    }

    private sealed class FakeMedicineCsvService : BusinessOS.Pharmacy.Application.Abstractions.Medicines.IMedicineCsvService
    {
        public IReadOnlyList<string> Columns => ["MedicineCode", "BrandName", "SaleUnit"];

        public Task WriteTemplateAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineCsvPreview> PreviewAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineCsvPreview(0, 0, 0, []));

        public Task<BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineCsvImportResult> ImportAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BusinessOS.Pharmacy.Application.Abstractions.Medicines.MedicineCsvImportResult(0, 0, []));
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

        private static readonly PosTopProductItem[] TopProducts =
        [
            new("med-para", "PARA-500", "Paracetamol", "500 mg", "tablet", 148m, 20m),
            new("med-amox", "AMOX-250", "Amoxicillin", "250 mg", "capsule", 96m, 35m),
            new("med-ome", "OME-020", "Omeprazole", "20 mg", "capsule", 74m, 28m),
            new("med-vit", "VIT-D3", "Vitamin D3", "50000 IU", "softgel", 61m, 45m),
            new("med-para2", "PARA-500", "Paracetamol Syrup", "120 mg/5 ml", "bottle", 52m, 120m),
            new("med-met", "MET-850", "Metformin", "850 mg", "tablet", 47m, 18m),
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

        public Task<IReadOnlyList<PosTopProductItem>> GetTopProductsAsync(
            string stockLocationId,
            int take = 10,
            int days = 30,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PosTopProductItem>>(
                TopProducts
                    .OrderByDescending(x => x.QuantitySold)
                    .Take(Math.Clamp(take, 1, 50))
                    .ToList());
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
            var timeline = options.Period switch
            {
                "week" => Enumerable.Range(0, 7)
                    .Select(i => new DashboardSalesPoint(
                        i,
                        9800m + (i * 1700m),
                        18 + (i * 3),
                        new DateOnly(2026, 9, 25).AddDays(i).ToString("dd MMM", System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList(),
                "month" => Enumerable.Range(0, 30)
                    .Select(i => new DashboardSalesPoint(
                        i,
                        2400m + ((i % 7) * 2600m),
                        7 + (i % 9),
                        (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    .ToList(),
                "year" => Enumerable.Range(0, 12)
                    .Select(i => new DashboardSalesPoint(
                        i,
                        385000m + (i * 42000m),
                        820 + (i * 55),
                        System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat
                            .GetAbbreviatedMonthName(i + 1)))
                    .ToList(),
                _ => new List<DashboardSalesPoint>
                {
                    new(8,500,2), new(9,2800,4), new(10,3600,5), new(11,2600,4),
                    new(12,1700,3), new(13,2200,4), new(14,2900,5), new(15,4100,6),
                    new(16,5200,7), new(17,4300,6), new(18,3900,5), new(19,7400,7),
                },
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
