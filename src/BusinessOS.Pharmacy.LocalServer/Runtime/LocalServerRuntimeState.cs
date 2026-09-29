using BusinessOS.Pharmacy.Application.Abstractions.Networking;

namespace BusinessOS.Pharmacy.LocalServer.Runtime;

public sealed class LocalServerRuntimeState
{
    private readonly object _gate = new();

    public LocalServerIdentity? Identity { get; private set; }
    public NetworkConfiguration? Configuration { get; private set; }
    public string? CertificateSha256 { get; private set; }

    public void Initialize(
        LocalServerIdentity identity,
        NetworkConfiguration configuration,
        string certificateSha256)
    {
        lock (_gate)
        {
            if (Identity is not null)
            {
                throw new InvalidOperationException("Local server runtime state is already initialized.");
            }

            Identity = identity;
            Configuration = configuration;
            CertificateSha256 = certificateSha256;
        }
    }

    public (LocalServerIdentity Identity, NetworkConfiguration Configuration, string CertificateSha256) Require()
    {
        lock (_gate)
        {
            return (
                Identity ?? throw new InvalidOperationException("Local server identity is not initialized."),
                Configuration ?? throw new InvalidOperationException("Local server configuration is not initialized."),
                CertificateSha256 ?? throw new InvalidOperationException("Local server certificate is not initialized."));
        }
    }
}
