using System.Threading.RateLimiting;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
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
builder.Services.AddScoped<IPermissionAuthorizer, RequestPermissionAuthorizer>();
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

await app.RunAsync();
