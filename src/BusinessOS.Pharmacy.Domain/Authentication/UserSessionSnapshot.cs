namespace BusinessOS.Pharmacy.Domain.Authentication;

public sealed record UserSessionSnapshot(
    string UserId,
    string TenantId,
    string ActivationId,
    string DeviceId,
    string Name,
    string Email,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt)
{
    public bool IsValidAt(DateTimeOffset trustedNow) =>
        trustedNow >= IssuedAt && trustedNow < ExpiresAt;

    public bool HasPermission(string permission) =>
        !string.IsNullOrWhiteSpace(permission) && Permissions.Contains(permission);
}
