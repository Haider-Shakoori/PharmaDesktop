namespace BusinessOS.Pharmacy.Application.Abstractions.Administration;

public interface IAccessManagementService
{
    Task<AccessManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task<AccessManagementSnapshot> CreateUserAsync(SaveAccessUserRequest request, CancellationToken cancellationToken = default);
    Task<AccessManagementSnapshot> UpdateUserAsync(int userId, SaveAccessUserRequest request, CancellationToken cancellationToken = default);
    Task<AccessManagementSnapshot> CreateRoleAsync(SaveAccessRoleRequest request, CancellationToken cancellationToken = default);
    Task<AccessManagementSnapshot> UpdateRoleAsync(int roleId, IReadOnlyList<int> permissionIds, CancellationToken cancellationToken = default);
}

public sealed record AccessManagementSnapshot(
    IReadOnlyList<AccessUserItem> Users,
    IReadOnlyList<AccessRoleItem> Roles,
    IReadOnlyList<AccessPermissionItem> Permissions);

public sealed record AccessUserItem(
    int Id,
    string Name,
    string Email,
    bool IsActive,
    IReadOnlyList<AccessRoleReference> Roles);

public sealed record AccessRoleReference(int Id, string Name, string Code);

public sealed record AccessRoleItem(
    int Id,
    string Name,
    string Code,
    bool IsSystem,
    int UsersCount,
    IReadOnlyList<AccessPermissionItem> Permissions);

public sealed record AccessPermissionItem(
    int Id,
    string Code,
    string Name,
    string? Description);

public sealed record SaveAccessUserRequest(
    string Name,
    string Email,
    string? Password,
    bool IsActive,
    IReadOnlyList<int> RoleIds);

public sealed record SaveAccessRoleRequest(
    string Name,
    string Code,
    IReadOnlyList<int> PermissionIds);
