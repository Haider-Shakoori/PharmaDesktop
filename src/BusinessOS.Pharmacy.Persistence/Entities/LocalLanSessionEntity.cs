namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class LocalLanSessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string TerminalId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RolesJson { get; set; } = "[]";
    public string PermissionsJson { get; set; } = "[]";
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
