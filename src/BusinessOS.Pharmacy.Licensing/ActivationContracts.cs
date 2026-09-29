using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessOS.Pharmacy.Licensing;

public sealed record LicenseActivationRequest(
    [property: JsonPropertyName("license_key")] string LicenseKey,
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("device_name")] string? DeviceName,
    [property: JsonPropertyName("app_version")] string? AppVersion,
    [property: JsonPropertyName("platform")] string Platform = "windows");

public sealed record LicenseActivationEnvelope(
    [property: JsonPropertyName("data")] LicenseActivationData Data,
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime);

public sealed record LicenseActivationData(
    [property: JsonPropertyName("activation_id")] string ActivationId,
    [property: JsonPropertyName("lease_token")] string LeaseToken,
    [property: JsonPropertyName("lease_expires_at")] DateTimeOffset LeaseExpiresAt,
    [property: JsonPropertyName("subscription_health")] string SubscriptionHealth,
    [property: JsonPropertyName("tenant")] TenantSummary Tenant,
    [property: JsonPropertyName("plan")] PlanSummary Plan);

public sealed record TenantSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("cloud_base_url")] string CloudBaseUrl,
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("locale")] string Locale);

public sealed record PlanSummary(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("max_android_devices")] int? MaxAndroidDevices,
    [property: JsonPropertyName("max_windows_devices")] int? MaxWindowsDevices,
    [property: JsonPropertyName("offline_grace_days")] int OfflineGraceDays,
    [property: JsonPropertyName("features")] JsonElement Features);
