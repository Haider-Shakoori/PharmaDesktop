using System.Net.Http.Headers;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanApiRequestFactory(
    PinnedLocalServerTransport transport,
    LanClientSessionState sessionState)
{
    public async Task<(HttpClient Client, HttpRequestMessage Request)> CreateAsync(
        HttpMethod method,
        string relativeUri,
        CancellationToken cancellationToken = default)
    {
        var state = sessionState.Get()
            ?? throw new InvalidOperationException(
                "A pharmacy user must be signed in to use this Client Terminal.");

        if (DateTimeOffset.UtcNow >= state.User.ExpiresAt)
        {
            throw new InvalidOperationException(
                "The LAN pharmacy-user session has expired. Sign in again.");
        }

        var connection = await transport.GetPairedConnectionAsync(cancellationToken);
        var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.TryAddWithoutValidation(
            "X-BusinessOS-Terminal-Id",
            connection.Pairing.TerminalId);
        request.Headers.TryAddWithoutValidation(
            "X-BusinessOS-Terminal-Secret",
            connection.Pairing.TerminalSecret);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", state.AccessToken);

        return (connection.Client, request);
    }
}
