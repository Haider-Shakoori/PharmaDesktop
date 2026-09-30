namespace BusinessOS.Pharmacy.Application.Abstractions.Networking;

public sealed record LocalServerIdentity(
    string ServerId,
    string TenantId,
    string ServerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RegisteredTerminal(
    string TerminalId,
    string Name,
    string ComputerName,
    string TerminalRole,
    bool IsActive,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    IReadOnlySet<string> AllowedPermissions);

public sealed record PairingCodeIssue(
    string PairingId,
    string Code,
    DateTimeOffset ExpiresAt);

public sealed record PairTerminalRequest(
    string PairingCode,
    string TerminalId,
    string Name,
    string ComputerName,
    string TerminalRole);

public sealed record PairTerminalResult(
    string TerminalId,
    string TerminalSecret,
    string ServerId,
    string TenantId,
    DateTimeOffset RegisteredAt);

public sealed record LocalServerDiscoveryAdvertisement(
    string Service,
    string ApiVersion,
    string ServerId,
    string ServerName,
    string HostName,
    int Port,
    string CertificateSha256);

public sealed record LocalServerHealth(
    bool Available,
    string ApiVersion,
    string ServerId,
    DateTimeOffset ServerTime);

public sealed record LocalServerConnectionStatus(
    bool IsConnected,
    string? ServerId,
    string? ServerName,
    TimeSpan? Latency,
    DateTimeOffset? LastSeenAt,
    string? Message);
