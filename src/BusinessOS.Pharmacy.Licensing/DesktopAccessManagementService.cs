using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessOS.Pharmacy.Application.Abstractions.Administration;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class DesktopAccessManagementService : IAccessManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSessionStore _sessions;

    public DesktopAccessManagementService(
        IHttpClientFactory httpClientFactory,
        IUserSessionStore sessions)
    {
        _httpClientFactory = httpClientFactory;
        _sessions = sessions;
    }

    public Task<AccessManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "/api/v1/desktop/access", null, cancellationToken);

    public Task<AccessManagementSnapshot> CreateUserAsync(
        SaveAccessUserRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Post,
            "/api/v1/desktop/access/users",
            new UserPayload(request.Name, request.Email, request.Password, request.IsActive, request.RoleIds),
            cancellationToken);

    public Task<AccessManagementSnapshot> UpdateUserAsync(
        int userId,
        SaveAccessUserRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Put,
            $"/api/v1/desktop/access/users/{userId}",
            new UserPayload(request.Name, request.Email, request.Password, request.IsActive, request.RoleIds),
            cancellationToken);

    public Task<AccessManagementSnapshot> CreateRoleAsync(
        SaveAccessRoleRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Post,
            "/api/v1/desktop/access/roles",
            new RolePayload(request.Name, request.Code, request.PermissionIds),
            cancellationToken);

    public Task<AccessManagementSnapshot> UpdateRoleAsync(
        int roleId,
        IReadOnlyList<int> permissionIds,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Put,
            $"/api/v1/desktop/access/roles/{roleId}",
            new PermissionPayload(permissionIds),
            cancellationToken);

    private async Task<AccessManagementSnapshot> SendAsync(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken cancellationToken)
    {
        var session = await _sessions.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sign in online before managing pharmacy users and roles.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: JsonOptions);

        var client = _httpClientFactory.CreateClient(DependencyInjection.HttpClientName);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(text, (int) response.StatusCode));

        try
        {
            var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(text, JsonOptions)
                ?? throw new InvalidOperationException("The Darmaltoon access service returned an empty response.");
            return Map(envelope.Data);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The Darmaltoon access service returned an invalid response.",
                exception);
        }
    }

    private static AccessManagementSnapshot Map(SnapshotDto data) =>
        new(
            data.Users.Select(x => new AccessUserItem(
                x.Id,
                x.Name,
                x.Email,
                x.IsActive,
                x.Roles.Select(role => new AccessRoleReference(role.Id, role.Name, role.Code)).ToList()))
                .ToList(),
            data.Roles.Select(x => new AccessRoleItem(
                x.Id,
                x.Name,
                x.Code,
                x.IsSystem,
                x.UsersCount,
                x.Permissions.Select(MapPermission).ToList()))
                .ToList(),
            data.Permissions.Select(MapPermission).ToList());

    private static AccessPermissionItem MapPermission(PermissionDto x) =>
        new(x.Id, x.Code, x.Name, x.Description);

    private static string ReadError(string text, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(message.GetString()))
                return message.GetString()!;

            if (document.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        continue;
                    var first = property.Value.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.String)
                        return first.GetString() ?? $"Request failed with HTTP {statusCode}.";
                }
            }
        }
        catch (JsonException)
        {
        }

        return $"Darmaltoon access management failed with HTTP {statusCode}.";
    }

    private sealed record SnapshotEnvelope(
        [property: JsonPropertyName("data")] SnapshotDto Data);

    private sealed record SnapshotDto(
        [property: JsonPropertyName("users")] IReadOnlyList<UserDto> Users,
        [property: JsonPropertyName("roles")] IReadOnlyList<RoleDto> Roles,
        [property: JsonPropertyName("permissions")] IReadOnlyList<PermissionDto> Permissions);

    private sealed record UserDto(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("roles")] IReadOnlyList<RoleReferenceDto> Roles);

    private sealed record RoleReferenceDto(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("code")] string Code);

    private sealed record RoleDto(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("is_system")] bool IsSystem,
        [property: JsonPropertyName("users_count")] int UsersCount,
        [property: JsonPropertyName("permissions")] IReadOnlyList<PermissionDto> Permissions);

    private sealed record PermissionDto(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description);

    private sealed record UserPayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string? Password,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("role_ids")] IReadOnlyList<int> RoleIds);

    private sealed record RolePayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("permission_ids")] IReadOnlyList<int> PermissionIds);

    private sealed record PermissionPayload(
        [property: JsonPropertyName("permission_ids")] IReadOnlyList<int> PermissionIds);
}
