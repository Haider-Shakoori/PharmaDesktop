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
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string BackupsDirectory { get; }

    public string LogsDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
