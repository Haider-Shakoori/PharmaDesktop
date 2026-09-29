using BusinessOS.Pharmacy.Domain.Authentication;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanClientSessionState
{
    private readonly object _gate = new();

    public string? AccessToken { get; private set; }
    public UserSessionSnapshot? User { get; private set; }

    public void Set(string accessToken, UserSessionSnapshot user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(user);

        lock (_gate)
        {
            AccessToken = accessToken;
            User = user;
        }
    }

    public (string AccessToken, UserSessionSnapshot User)? Get()
    {
        lock (_gate)
        {
            return AccessToken is null || User is null
                ? null
                : (AccessToken, User);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            AccessToken = null;
            User = null;
        }
    }
}
