namespace BusinessOS.Pharmacy.Application.Abstractions.Authentication;

public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string permission)
        : base($"The current pharmacy user does not have permission '{permission}'.")
    {
        Permission = permission;
    }

    public string Permission { get; }
}
