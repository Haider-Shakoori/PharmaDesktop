namespace BusinessOS.Pharmacy.Application.Abstractions.Authentication;

public interface IPermissionAuthorizer
{
    bool HasPermission(string permission);
    void Demand(string permission);
}
