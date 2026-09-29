using BusinessOS.Pharmacy.Domain.Authentication;

namespace BusinessOS.Pharmacy.Application.Abstractions.Authentication;

public interface IUserSessionService
{
    UserSessionSnapshot? Current { get; }

    Task<UserSessionSnapshot> LoginAsync(
        string email,
        string password,
        bool allowOfflineSignIn,
        CancellationToken cancellationToken = default);

    Task<UserSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
