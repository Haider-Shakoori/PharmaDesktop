namespace BusinessOS.Pharmacy.Persistence.Entities;

internal sealed class LocalLanUserCredentialEntity
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RolesJson { get; set; } = "[]";
    public string PermissionsJson { get; set; } = "[]";
    public string PasswordSaltBase64 { get; set; } = string.Empty;
    public string PasswordHashBase64 { get; set; } = string.Empty;
    public int PasswordIterations { get; set; }
    public DateTimeOffset LastOnlineVerifiedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
