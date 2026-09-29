using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.Infrastructure.Storage;

public sealed class ApplicationPaths : IApplicationPaths
{
    public ApplicationPaths(string? rootOverride = null)
    {
        var root = rootOverride;

        if (string.IsNullOrWhiteSpace(root))
        {
            var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            root = Path.Combine(commonApplicationData, "BusinessOS", "Pharmacy");
        }

        RootDirectory = Path.GetFullPath(root);
        DatabasePath = Path.Combine(RootDirectory, "pharmacy.db");
        BackupsDirectory = Path.Combine(RootDirectory, "backups");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        LicensingDirectory = Path.Combine(RootDirectory, "licensing");
        InstallationIdPath = Path.Combine(LicensingDirectory, "installation.id");
        ActivationStatePath = Path.Combine(LicensingDirectory, "activation.bin");
        UserSessionStatePath = Path.Combine(LicensingDirectory, "user-session.bin");
    }

    public string RootDirectory { get; }
    public string DatabasePath { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string LicensingDirectory { get; }
    public string InstallationIdPath { get; }
    public string ActivationStatePath { get; }
    public string UserSessionStatePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(LicensingDirectory);
    }
}
