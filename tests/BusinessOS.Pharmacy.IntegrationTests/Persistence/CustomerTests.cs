using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Customers;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class CustomerTests
{
    [Fact]
    public async Task Customer_create_update_and_search_preserve_four_decimal_credit_limit()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var customers = provider.GetRequiredService<ICustomerService>();

            var id = await customers.CreateAsync(
                new SaveCustomerRequest(
                    "Ahmad Pharmacy Customer",
                    "0700123456",
                    "ahmad@example.test",
                    100.12345m,
                    true,
                    "Regular customer"));

            var created = await customers.GetAsync(id);
            Assert.NotNull(created);
            Assert.Equal("Ahmad Pharmacy Customer", created!.Name);
            Assert.Equal(100.1235m, created.CreditLimit);
            Assert.True(created.IsActive);

            await customers.UpdateAsync(
                id,
                new SaveCustomerRequest(
                    "Ahmad Updated",
                    "0700999999",
                    "ahmad@example.test",
                    250.5m,
                    true,
                    "Updated credit"));

            var search = await customers.SearchAsync(
                new CustomerSearchFilter("0700999999", IsActive: true));

            var updated = Assert.Single(search);
            Assert.Equal(id, updated.Id);
            Assert.Equal("Ahmad Updated", updated.Name);
            Assert.Equal(250.5m, updated.CreditLimit);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Customer_summary_reports_counts_and_total_credit_limit()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var customers = provider.GetRequiredService<ICustomerService>();

            await customers.CreateAsync(
                new SaveCustomerRequest(
                    "Active Customer",
                    "0700111111",
                    null,
                    150.25m,
                    true,
                    null));

            await customers.CreateAsync(
                new SaveCustomerRequest(
                    "Inactive Customer",
                    "0700222222",
                    null,
                    49.75m,
                    false,
                    null));

            var summary = await customers.GetSummaryAsync();

            Assert.Equal(2, summary.TotalCustomers);
            Assert.Equal(1, summary.ActiveCustomers);
            Assert.Equal(1, summary.InactiveCustomers);
            Assert.Equal(200m, summary.TotalCreditLimit);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Customer_validation_matches_web_field_limits()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var customers = provider.GetRequiredService<ICustomerService>();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                customers.CreateAsync(
                    new SaveCustomerRequest(
                        "Invalid Email",
                        null,
                        "not-an-email",
                        0m,
                        true,
                        null)));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                customers.CreateAsync(
                    new SaveCustomerRequest(
                        "Invalid Credit",
                        null,
                        null,
                        -0.0001m,
                        true,
                        null)));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                customers.CreateAsync(
                    new SaveCustomerRequest(
                        new string('X', 181),
                        null,
                        null,
                        0m,
                        true,
                        null)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Credit_policy_caps_credit_for_a_single_sale_and_requires_active_customer()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await InitializeAsync(provider);

            var customers = provider.GetRequiredService<ICustomerService>();
            var credit = provider.GetRequiredService<ICustomerCreditPolicy>();

            var id = await customers.CreateAsync(
                new SaveCustomerRequest(
                    "Credit Customer",
                    "0700000000",
                    null,
                    500m,
                    true,
                    null));

            var allowed = await credit.ValidateAsync(id, 300m);
            Assert.Equal(id, allowed.Id);
            Assert.Equal(500m, allowed.CreditLimit);
            Assert.Equal(300m, allowed.RequestedCredit);
            Assert.Equal(200m, allowed.RemainingPerSaleCapacity);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                credit.ValidateAsync(id, 500.0001m));

            await customers.UpdateAsync(
                id,
                new SaveCustomerRequest(
                    "Credit Customer",
                    "0700000000",
                    null,
                    500m,
                    false,
                    null));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                credit.ValidateAsync(id, 1m));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task InitializeAsync(ServiceProvider provider)
    {
        await provider.GetRequiredService<ILocalDatabaseInitializer>()
            .InitializeAsync("tenant-customers");
    }

    private static ServiceProvider BuildProvider(string root)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);
        services.AddSingleton<IPermissionAuthorizer>(new AllowAllPermissionAuthorizer());
        services.AddSingleton<IUserSessionService>(new TestUserSessionService());
        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-customer-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AllowAllPermissionAuthorizer : IPermissionAuthorizer
    {
        public bool HasPermission(string permission) => true;

        public void Demand(string permission)
        {
        }
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private static readonly UserSessionSnapshot Session = new(
            "user-customers",
            "tenant-customers",
            "activation-customers",
            "device-customers",
            "Customer Tester",
            "customers@test.local",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "customers" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "customers.manage",
                "pos.sell",
            },
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(8));

        public UserSessionSnapshot? Current => Session;

        public Task<UserSessionSnapshot> LoginAsync(
            string email,
            string password,
            bool allowOfflineSignIn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public Task<UserSessionSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Session);

        public Task LogoutAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
