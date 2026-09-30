using BusinessOS.Pharmacy.Application.Abstractions.Sales;
using BusinessOS.Pharmacy.Application.Abstractions.Reports;
using BusinessOS.Pharmacy.Application.Abstractions.Purchasing;
using BusinessOS.Pharmacy.Application.Abstractions.Inventory;
using BusinessOS.Pharmacy.Application.Abstractions.DailyClosing;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.Accounting;
using System.Threading.RateLimiting;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Networking;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Licensing;
using BusinessOS.Pharmacy.LocalServer.Api;
using BusinessOS.Pharmacy.LocalServer.Authentication;
using BusinessOS.Pharmacy.LocalServer.Discovery;
using BusinessOS.Pharmacy.LocalServer.Logging;
using BusinessOS.Pharmacy.LocalServer.Runtime;
using BusinessOS.Pharmacy.LocalServer.Security;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.AspNetCore.RateLimiting;

var paths = new ApplicationPaths();
paths.EnsureCreated();

var bootstrapConfigurationStore = new NetworkConfigurationStore(paths);
var networkConfiguration = await bootstrapConfigurationStore.LoadAsync();

if (networkConfiguration.Mode != DeploymentMode.Server)
{
    throw new InvalidOperationException(
        "BusinessOS Pharmacy Local Server can run only when this computer is configured as Main Pharmacy Server.");
}

if (OperatingSystem.IsWindows())
{
    var networkProfile = await new WindowsLocalServerServiceController()
        .GetNetworkProfileStatusAsync();

    if (networkProfile.HasPublicNetwork &&
        !networkProfile.HasPrivateOrDomainNetwork)
    {
        throw new InvalidOperationException(
            "Darmaltoon Local Server will not listen on a Public-only Windows network. Mark the trusted pharmacy LAN as Private, then start the service again.");
    }
}

var bootstrapSecrets = new WindowsNetworkSecretStore(paths);
var certificateProvider = new LocalServerCertificateProvider(paths, bootstrapSecrets);
using var certificate = await certificateProvider.GetOrCreateAsync();
var certificateSha256 = LocalServerCertificateProvider.Sha256Fingerprint(certificate);

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "BusinessOS Pharmacy Local Server";
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(networkConfiguration.ServerPort, listen =>
    {
        listen.UseHttps(certificate);
    });
});

builder.Services.AddBusinessOSInfrastructure(paths);
builder.Services.AddBusinessOSPersistence();
builder.Services.AddBusinessOSLicensing(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IUserSessionService, RequestUserSessionService>();
builder.Services.AddSingleton<IPermissionAuthorizer, RequestPermissionAuthorizer>();
builder.Services.AddScoped<TerminalAuthenticationFilter>();
builder.Services.AddScoped<UserSessionAuthenticationFilter>();
builder.Services.AddScoped<LanUserAuthenticationService>();

builder.Services.AddSingleton<LocalServerRuntimeState>();
builder.Services.AddSingleton<NetworkFileLogger>();
builder.Services.AddHostedService<UdpDiscoveryResponder>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("pairing", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("lan", limiter =>
    {
        limiter.PermitLimit = 300;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 20;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();

app.UseRateLimiter();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (PermissionDeniedException)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "This pharmacy user does not have permission for this operation."
        });
    }
    catch (LicenseApiException exception)
    {
        context.Response.StatusCode = exception.IsRetryable
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status401Unauthorized;

        await context.Response.WriteAsJsonAsync(new { message = exception.Message });
    }
    catch (UnauthorizedAccessException)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "This pharmacy user is not authorized for this operation." });
    }
    catch (ArgumentException exception)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { message = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { message = exception.Message });
    }
});

var entitlement = await app.Services
    .GetRequiredService<ILicenseService>()
    .GetCachedEntitlementAsync();

if (entitlement is null ||
    !entitlement.IsOfflineLeaseValidAt(DateTimeOffset.UtcNow))
{
    throw new InvalidOperationException(
        "The Main Pharmacy Server requires a valid BusinessOS pharmacy activation before the LAN service can start.");
}

await app.Services
    .GetRequiredService<ILocalDatabaseInitializer>()
    .InitializeAsync(entitlement.TenantId);

var identity = await app.Services
    .GetRequiredService<ILocalTerminalService>()
    .GetOrCreateServerIdentityAsync(
        entitlement.TenantId,
        networkConfiguration.ServerName);

networkConfiguration = networkConfiguration with
{
    ServerHost = Environment.MachineName,
    ServerId = identity.ServerId,
    ServerCertificateSha256 = certificateSha256,
    IsConfigured = true,
};

await app.Services
    .GetRequiredService<INetworkConfigurationStore>()
    .SaveAsync(networkConfiguration);

app.Services
    .GetRequiredService<LocalServerRuntimeState>()
    .Initialize(identity, networkConfiguration, certificateSha256);

var networkLog = app.Services.GetRequiredService<NetworkFileLogger>();
await networkLog.WriteAsync(
    "server.started",
    $"server_id={identity.ServerId}; host={Environment.MachineName}; port={networkConfiguration.ServerPort}");

app.Lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        networkLog.WriteAsync(
                "server.stopping",
                $"server_id={identity.ServerId}")
            .GetAwaiter()
            .GetResult();
    }
    catch
    {
        // Shutdown logging must never block service termination.
    }
});

var local = app.MapGroup("/api/local/v1")
    .RequireRateLimiting("lan");

local.MapGet("/health", (LocalServerRuntimeState runtime) =>
{
    var state = runtime.Require();
    return Results.Ok(new LocalServerHealth(
        true,
        "v1",
        state.Identity.ServerId,
        DateTimeOffset.UtcNow));
});

local.MapGet("/server-info", (LocalServerRuntimeState runtime) =>
{
    var state = runtime.Require();
    return Results.Ok(new LocalServerInfoResponse(
        "BusinessOS.Pharmacy.LocalServer",
        "v1",
        "1.0.0",
        state.Identity.ServerId,
        state.Identity.ServerName,
        Environment.MachineName,
        state.Configuration.ServerPort,
        state.CertificateSha256));
});

local.MapPost(
        "/pairing/complete",
        async (
            PairTerminalApiRequest request,
            ILocalTerminalService terminals,
            LocalServerRuntimeState runtime,
            NetworkFileLogger fileLog,
            CancellationToken cancellationToken) =>
        {
            var paired = await terminals.PairAsync(
                request.ToApplication(),
                cancellationToken);

            var state = runtime.Require();
            if (!string.Equals(
                    paired.TenantId,
                    state.Identity.TenantId,
                    StringComparison.Ordinal))
            {
                return Results.Conflict(new
                {
                    message = "The paired terminal does not belong to this pharmacy tenant."
                });
            }

            await fileLog.WriteAsync(
                "terminal.paired",
                $"terminal_id={paired.TerminalId}",
                cancellationToken);

            return Results.Ok(new PairTerminalApiResponse(
                paired.TerminalId,
                paired.TerminalSecret,
                paired.ServerId,
                paired.TenantId,
                paired.RegisteredAt));
        })
    .RequireRateLimiting("pairing");

var terminal = local.MapGroup(string.Empty);
terminal.AddEndpointFilter<TerminalAuthenticationFilter>();

terminal.MapPost(
    "/auth/login",
    async (
        LocalLoginRequest request,
        HttpContext http,
        LanUserAuthenticationService authentication,
        NetworkFileLogger fileLog,
        CancellationToken cancellationToken) =>
    {
        var registered = (RegisteredTerminal)http.Items[LanRequestKeys.Terminal]!;
        var result = await authentication.LoginAsync(
            registered.TerminalId,
            request.Email,
            request.Password,
            cancellationToken);

        await fileLog.WriteAsync(
            "user.login",
            $"terminal_id={registered.TerminalId}; user_id={result.User.Id}",
            cancellationToken);

        return Results.Ok(result);
    });

terminal.MapPost(
    "/terminals/heartbeat",
    (HttpContext http) =>
    {
        var registered = (RegisteredTerminal)http.Items[LanRequestKeys.Terminal]!;
        return Results.Ok(new TerminalHeartbeatResponse(
            registered.TerminalId,
            DateTimeOffset.UtcNow));
    });

var authorized = terminal.MapGroup(string.Empty);
authorized.AddEndpointFilter<UserSessionAuthenticationFilter>();

authorized.MapGet(
    "/medicines",
    async (
        string? search,
        string? categoryId,
        string? status,
        int? take,
        IMedicineCatalogService medicines,
        CancellationToken cancellationToken) =>
    {
        bool? active = status?.ToLowerInvariant() switch
        {
            "active" => true,
            "inactive" => false,
            _ => null,
        };

        var result = await medicines.SearchAsync(
            new MedicineSearchFilter(
                search,
                categoryId,
                active,
                Math.Clamp(take ?? 250, 1, 1000)),
            cancellationToken);

        return Results.Ok(result);
    });

authorized.MapGet(
    "/medicines/references",
    async (
        IMedicineCatalogService medicines,
        CancellationToken cancellationToken) =>
        Results.Ok(await medicines.GetReferenceDataAsync(cancellationToken)));

authorized.MapGet(
    "/medicines/{id}",
    async (
        string id,
        IMedicineCatalogService medicines,
        CancellationToken cancellationToken) =>
    {
        var result = await medicines.GetAsync(id, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

authorized.MapPost(
    "/medicines",
    async (
        SaveMedicineRequest request,
        IMedicineCatalogService medicines,
        CancellationToken cancellationToken) =>
    {
        var id = await medicines.CreateAsync(request, cancellationToken);
        return Results.Ok(new MedicineCreateResponse(id));
    });

authorized.MapPost(
    "/medicine-categories",
    async (
        CreateCategoryRequest request,
        IMedicineCatalogService medicines,
        CancellationToken cancellationToken) =>
    {
        var id = await medicines.CreateCategoryAsync(request.Name, cancellationToken);
        return Results.Ok(new MedicineCreateResponse(id));
    });

authorized.MapPost(
    "/manufacturers",
    async (
        CreateManufacturerRequest request,
        IMedicineCatalogService medicines,
        CancellationToken cancellationToken) =>
    {
        var id = await medicines.CreateManufacturerAsync(
            request.Name,
            request.Country,
            cancellationToken);
        return Results.Ok(new MedicineCreateResponse(id));
    });

authorized.MapGet(
    "/dashboard",
    async (
        DateOnly businessDate,
        int? lowStockThreshold,
        int? nearExpiryDays,
        IPermissionAuthorizer permissions,
        ILocalDashboardQueryService dashboard,
        CancellationToken cancellationToken) =>
    {
        permissions.Demand("dashboard.view");

        var snapshot = await dashboard.GetSnapshotAsync(
            new DashboardQueryOptions(
                businessDate,
                lowStockThreshold ?? 10,
                nearExpiryDays ?? 90),
            cancellationToken);

        return Results.Ok(snapshot);
    });

authorized.MapPut(
    "/medicines/{id}",
    async (
        string id,
        SaveMedicineRequest request,
        IMedicineCatalogService medicines,
        CancellationToken cancellationToken) =>
    {
        await medicines.UpdateAsync(id, request, cancellationToken);
        return Results.NoContent();
    });


// Batch 18 operational LAN surface. Business rules remain server-authoritative.
authorized.MapPost("/inventory/defaults", async (IInventoryService service, CancellationToken ct) => { await service.EnsureDefaultsAsync(ct); return Results.NoContent(); });
authorized.MapGet("/inventory/references", async (IInventoryService service, CancellationToken ct) => Results.Ok(await service.GetReferenceDataAsync(ct)));
authorized.MapPost("/inventory/batches/search", async (InventoryBatchFilter filter, IInventoryService service, CancellationToken ct) => Results.Ok(await service.SearchBatchesAsync(filter, ct)));
authorized.MapGet("/inventory/batches/{id}", async (string id, IInventoryService service, CancellationToken ct) => { var result = await service.GetBatchAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });
authorized.MapPost("/inventory/opening-stock", async (CreateOpeningStockRequest request, IInventoryService service, CancellationToken ct) => Results.Ok(new MedicineCreateResponse(await service.CreateOpeningStockAsync(request, ct))));
authorized.MapPost("/inventory/adjust", async (InventoryAdjustmentRequest request, IInventoryService service, CancellationToken ct) => Results.Ok(await service.AdjustAsync(request, ct)));
authorized.MapPost("/inventory/change-status", async (ChangeBatchStatusRequest request, IInventoryService service, CancellationToken ct) => { await service.ChangeBatchStatusAsync(request, ct); return Results.NoContent(); });

authorized.MapPost("/suppliers/search", async (SupplierSearchFilter filter, ISupplierService service, CancellationToken ct) => Results.Ok(await service.SearchAsync(filter, ct)));
authorized.MapGet("/suppliers/{id}", async (string id, ISupplierService service, CancellationToken ct) => { var result = await service.GetAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });
authorized.MapPost("/suppliers", async (SaveSupplierRequest request, ISupplierService service, CancellationToken ct) => Results.Ok(new MedicineCreateResponse(await service.CreateAsync(request, ct))));
authorized.MapPut("/suppliers/{id}", async (string id, SaveSupplierRequest request, ISupplierService service, CancellationToken ct) => { await service.UpdateAsync(id, request, ct); return Results.NoContent(); });

authorized.MapGet("/purchasing/references", async (IPurchasingService service, CancellationToken ct) => Results.Ok(await service.GetReferenceDataAsync(ct)));
authorized.MapPost("/purchasing/orders/search", async (PurchaseOrderSearchFilter filter, IPurchasingService service, CancellationToken ct) => Results.Ok(await service.SearchOrdersAsync(filter, ct)));
authorized.MapGet("/purchasing/orders/{id}", async (string id, IPurchasingService service, CancellationToken ct) => { var result = await service.GetOrderAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });
authorized.MapPost("/purchasing/orders", async (CreatePurchaseOrderRequest request, IPurchasingService service, CancellationToken ct) => Results.Ok(new MedicineCreateResponse(await service.CreateOrderAsync(request, ct))));
authorized.MapPost("/purchasing/orders/{id}/submit", async (string id, IPurchasingService service, CancellationToken ct) => { await service.SubmitOrderAsync(id, ct); return Results.NoContent(); });
authorized.MapPost("/purchasing/orders/{id}/approve", async (string id, IPurchasingService service, CancellationToken ct) => { await service.ApproveOrderAsync(id, ct); return Results.NoContent(); });
authorized.MapPost("/purchasing/orders/{id}/cancel", async (string id, IPurchasingService service, CancellationToken ct) => { await service.CancelOrderAsync(id, ct); return Results.NoContent(); });
authorized.MapPost("/purchasing/orders/{id}/receipts", async (string id, CaptureGoodsReceiptRequest request, IPurchasingService service, CancellationToken ct) => Results.Ok(new MedicineCreateResponse(await service.CaptureGoodsReceiptAsync(id, request, ct))));
authorized.MapPost("/purchasing/receipts/{id}/post", async (string id, StockLocationApiRequest request, IPurchasingService service, CancellationToken ct) => { await service.PostGoodsReceiptAsync(id, request.StockLocationId, ct); return Results.NoContent(); });
authorized.MapPost("/purchasing/orders/{id}/invoices", async (string id, CreatePurchaseInvoiceRequest request, IPurchasingService service, CancellationToken ct) => Results.Ok(new MedicineCreateResponse(await service.CreateInvoiceAsync(id, request, ct))));
authorized.MapPost("/purchasing/invoices/{id}/payments", async (string id, RecordSupplierPaymentRequest request, IPurchasingService service, CancellationToken ct) => Results.Ok(new MedicineCreateResponse(await service.RecordSupplierPaymentAsync(id, request, ct))));

authorized.MapPost("/customers/search", async (CustomerSearchFilter filter, ICustomerService service, CancellationToken ct) => Results.Ok(await service.SearchAsync(filter, ct)));
authorized.MapGet("/customers/{id}", async (string id, ICustomerService service, CancellationToken ct) => { var result = await service.GetAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });
authorized.MapPost("/customers", async (SaveCustomerRequest request, ICustomerService service, CancellationToken ct) => Results.Ok(new MedicineCreateResponse(await service.CreateAsync(request, ct))));
authorized.MapPut("/customers/{id}", async (string id, SaveCustomerRequest request, ICustomerService service, CancellationToken ct) => { await service.UpdateAsync(id, request, ct); return Results.NoContent(); });

authorized.MapGet("/pos/references", async (IPosService service, CancellationToken ct) => Results.Ok(await service.GetReferenceDataAsync(ct)));
authorized.MapPost("/pos/products/search", async (PosProductSearchFilter filter, IPosService service, CancellationToken ct) => Results.Ok(await service.SearchProductsAsync(filter, ct)));
authorized.MapPost("/pos/checkout", async (PosCheckoutRequest request, IPosService service, CancellationToken ct) => Results.Ok(await service.CheckoutAsync(request, ct)));
authorized.MapPost("/pos/sales/search", async (SaleSearchFilter filter, IPosService service, CancellationToken ct) => Results.Ok(await service.SearchSalesAsync(filter, ct)));
authorized.MapGet("/pos/sales/{id}", async (string id, IPosService service, CancellationToken ct) => { var result = await service.GetSaleAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });

authorized.MapPost("/returns/returnable-sales/search", async (SaleSearchFilter filter, ISaleReturnService service, CancellationToken ct) => Results.Ok(await service.SearchReturnableSalesAsync(filter, ct)));
authorized.MapGet("/returns/returnable-sales/{id}", async (string id, ISaleReturnService service, CancellationToken ct) => { var result = await service.GetReturnableSaleAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });
authorized.MapPost("/returns/process", async (ProcessSaleReturnRequest request, ISaleReturnService service, CancellationToken ct) => Results.Ok(await service.ProcessAsync(request, ct)));
authorized.MapPost("/returns/search", async (SaleReturnSearchFilter filter, ISaleReturnService service, CancellationToken ct) => Results.Ok(await service.SearchReturnsAsync(filter, ct)));
authorized.MapGet("/returns/{id}", async (string id, ISaleReturnService service, CancellationToken ct) => { var result = await service.GetReturnAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });

authorized.MapPost("/expenses/defaults", async (IExpenseService service, CancellationToken ct) => { await service.EnsureDefaultsAsync(ct); return Results.NoContent(); });
authorized.MapGet("/expenses/references", async (IExpenseService service, CancellationToken ct) => Results.Ok(await service.GetReferenceDataAsync(ct)));
authorized.MapPost("/expenses", async (PostExpenseRequest request, IExpenseService service, CancellationToken ct) => Results.Ok(await service.PostAsync(request, ct)));
authorized.MapPost("/expenses/{id}/reverse", async (string id, ReasonApiRequest request, IExpenseService service, CancellationToken ct) => Results.Ok(await service.ReverseAsync(id, request.Reason, ct)));
authorized.MapPost("/expenses/search", async (ExpenseSearchFilter filter, IExpenseService service, CancellationToken ct) => Results.Ok(await service.SearchAsync(filter, ct)));
authorized.MapGet("/expenses/{id}", async (string id, IExpenseService service, CancellationToken ct) => { var result = await service.GetAsync(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); });
authorized.MapGet("/expenses/{id}/journals", async (string id, IExpenseService service, CancellationToken ct) => Results.Ok(await service.GetJournalsAsync(id, ct)));

authorized.MapGet("/closing/references", async (IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.GetReferenceDataAsync(ct)));
authorized.MapPost("/closing/workspace", async (ClosingWorkspaceApiRequest request, IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.GetWorkspaceAsync(request.StockLocationId, request.BusinessDate, ct)));
authorized.MapPost("/closing/shifts/open", async (OpenShiftApiRequest request, IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.OpenShiftAsync(request.StockLocationId, request.OpeningCash, ct)));
authorized.MapPost("/closing/shifts/{id}/close", async (string id, CloseShiftApiRequest request, IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.CloseShiftAsync(id, request.CountedCash, request.Notes, ct)));
authorized.MapPost("/closing/finalize", async (FinalizeClosingApiRequest request, IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.FinalizeAsync(request.StockLocationId, request.CountedCash, request.Notes, ct)));
authorized.MapPost("/closing/{id}/approve", async (string id, IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.ApproveAsync(id, ct)));
authorized.MapPost("/closing/{id}/reopen", async (string id, ReasonApiRequest request, IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.ReopenAsync(id, request.Reason, ct)));
authorized.MapPost("/closing/sales-blocked", async (SalesBlockedApiRequest request, IDailyClosingService service, CancellationToken ct) => Results.Ok(await service.SalesBlockedAsync(request.StockLocationId, request.BusinessDate, ct)));

authorized.MapPost("/reports/workspace", async (ReportRange range, IPharmacyReportService service, CancellationToken ct) => Results.Ok(await service.GetAsync(range, ct)));
authorized.MapPost("/reports/csv", async (ReportCsvApiRequest request, IPharmacyReportService service, CancellationToken ct) => Results.Ok(await service.BuildCsvAsync(request.Type, request.Range, ct)));

await app.RunAsync();
