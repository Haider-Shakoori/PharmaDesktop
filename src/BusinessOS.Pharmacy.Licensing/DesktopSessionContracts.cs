using System.Text.Json.Serialization;

namespace BusinessOS.Pharmacy.Licensing;

public sealed record DesktopSessionLoginRequest(
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password);

public sealed record DesktopSessionRefreshRequest(
    [property: JsonPropertyName("device_id")] string DeviceId);

public sealed record DesktopSessionEnvelope(
    [property: JsonPropertyName("data")] DesktopSessionData Data,
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime);

public sealed record DesktopSessionData(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("access_expires_at")] DateTimeOffset AccessExpiresAt,
    [property: JsonPropertyName("subscription_health")] string SubscriptionHealth,
    [property: JsonPropertyName("user")] DesktopUserSummary User);

public sealed record DesktopUserSummary(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("roles")] string[] Roles,
    [property: JsonPropertyName("permissions")] string[] Permissions);
