namespace BusinessOS.Pharmacy.Application.Abstractions.Storage;

public interface IApplicationPaths
{
    string RootDirectory { get; }
    string DatabasePath { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string LicensingDirectory { get; }
    string ConfigDirectory { get; }
    string CertificatesDirectory { get; }
    string NetworkConfigurationPath { get; }
    string NetworkSecretsPath { get; }
    string ServerCertificatePath { get; }
    string NetworkLogPath { get; }
    string InstallationIdPath { get; }
    string ActivationStatePath { get; }
    string UserSessionStatePath { get; }

    void EnsureCreated();
}
