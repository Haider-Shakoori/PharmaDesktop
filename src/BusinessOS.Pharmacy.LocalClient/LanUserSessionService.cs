using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Domain.Authentication;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanUserSessionService : IUserSessionService
{
    private readonly PinnedLocalServerTransport _transport;
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly LanClientSessionState _state;

    public LanUserSessionService(
        PinnedLocalServerTransport transport,
        INetworkConfigurationStore configurationStore,
        LanClientSessionState state)
    {
        _transport = transport;
        _configurationStore = configurationStore;
        _state = state;
    }

    public UserSessionSnapshot? Current => _state.Get()?.User;

    public async Task<UserSessionSnapshot> LoginAsync(
        string email,
        string password,
        bool allowOfflineSignIn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var configuration = await _configurationStore.LoadAsync(cancellationToken);
        configuration.Validate();

        var client = await _transport.GetPairedClientAsync(cancellationToken);

        using var response = await client.PostAsJsonAsync(
            "auth/login",
            new LoginRequest(email.Trim(), password),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode || body is null)
        {
            throw new InvalidOperationException(
                body?.Message ??
                "The Main Pharmacy Server rejected the pharmacy-user login.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new UserSessionSnapshot(
            body.User.Id,
            configuration.TenantId!,
            configuration.ServerId!,
            configuration.TerminalId!,
            body.User.Name,
            body.User.Email,
            body.User.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase),
            body.User.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            now,
            body.ExpiresAt);

        _state.Set(body.AccessToken, user);
        return user;
    }

    public async Task<UserSessionSnapshot> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var current = _state.Get()
            ?? throw new InvalidOperationException("No Client Terminal user is signed in.");

        if (DateTimeOffset.UtcNow >= current.User.ExpiresAt)
        {
            _state.Clear();
            throw new InvalidOperationException(
                "The LAN pharmacy-user session has expired. Sign in again.");
        }

        var client = await _transport.GetPairedClientAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "terminals/heartbeat");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", current.AccessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return current.User;
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        _state.Clear();
        return Task.CompletedTask;
    }

    private sealed record LoginRequest(string Email, string Password);

    private sealed record LoginResponse(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        LoginUser User,
        string? Message = null);

    private sealed record LoginUser(
        string Id,
        string Name,
        string Email,
        string[] Roles,
        string[] Permissions);
}
