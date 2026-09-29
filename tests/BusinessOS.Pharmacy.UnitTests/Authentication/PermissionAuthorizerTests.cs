using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Domain.Authentication;
using BusinessOS.Pharmacy.Licensing;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Authentication;

public sealed class PermissionAuthorizerTests
{
    [Fact]
    public void Demand_allows_granted_permission_and_fails_closed_for_missing_permission()
    {
        var session = new UserSessionSnapshot(
            "1",
            "tenant-1",
            "activation-1",
            "device-1",
            "Cashier",
            "cashier@example.test",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cashier" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pos.sell" },
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));

        var sessions = new FakeUserSessionService(session);
        var authorizer = new PermissionAuthorizer(sessions);

        authorizer.Demand("pos.sell");
        Assert.True(authorizer.HasPermission("POS.SELL"));

        var error = Assert.Throws<PermissionDeniedException>(
            () => authorizer.Demand("inventory.adjust"));
        Assert.Equal("inventory.adjust", error.Permission);
    }

    private sealed class FakeUserSessionService : IUserSessionService
    {
        public FakeUserSessionService(UserSessionSnapshot current) => Current = current;

        public UserSessionSnapshot? Current { get; private set; }

        public Task<UserSessionSnapshot> LoginAsync(
            string email,
            string password,
            bool allowOfflineSignIn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current!);

        public Task<UserSessionSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current!);

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            Current = null;
            return Task.CompletedTask;
        }
    }
}
