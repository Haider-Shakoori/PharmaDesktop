using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.LocalServer.Api;

public sealed record LocalLoginRequest(
    string Email,
    string Password);

public sealed record LocalLoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    LocalUserResponse User);

public sealed record LocalUserResponse(
    string Id,
    string Name,
    string Email,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions);

public sealed record PairTerminalApiRequest(
    string PairingCode,
    string TerminalId,
    string Name,
    string ComputerName,
    string TerminalRole);

public sealed record PairTerminalApiResponse(
    string TerminalId,
    string TerminalSecret,
    string ServerId,
    string TenantId,
    DateTimeOffset RegisteredAt);

public sealed record LocalServerInfoResponse(
    string Service,
    string ApiVersion,
    string ApplicationVersion,
    string MinimumClientVersion,
    string ServerId,
    string ServerName,
    string HostName,
    int Port,
    string CertificateSha256);

public sealed record TerminalHeartbeatResponse(
    string TerminalId,
    DateTimeOffset ServerTime);

public sealed record MedicineCreateResponse(string Id);

public sealed record CreateCategoryRequest(string Name);

public sealed record CreateManufacturerRequest(
    string Name,
    string? Country);


public sealed record StockLocationApiRequest(string StockLocationId);
public sealed record ReasonApiRequest(string Reason);
public sealed record ClosingWorkspaceApiRequest(string StockLocationId, DateOnly? BusinessDate);
public sealed record OpenShiftApiRequest(string StockLocationId, decimal OpeningCash);
public sealed record CloseShiftApiRequest(decimal CountedCash, string? Notes);
public sealed record FinalizeClosingApiRequest(string StockLocationId, decimal? CountedCash, string? Notes);
public sealed record SalesBlockedApiRequest(string StockLocationId, DateOnly? BusinessDate);
public sealed record ReportCsvApiRequest(string Type, BusinessOS.Pharmacy.Application.Abstractions.Reports.ReportRange Range);

public static class LocalApiModelExtensions
{
    public static PairTerminalRequest ToApplication(this PairTerminalApiRequest request) =>
        new(
            request.PairingCode,
            request.TerminalId,
            request.Name,
            request.ComputerName,
            request.TerminalRole);
}
