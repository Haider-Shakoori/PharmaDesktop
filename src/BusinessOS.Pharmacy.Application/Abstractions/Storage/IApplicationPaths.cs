namespace BusinessOS.Pharmacy.Application.Abstractions.Storage;

public interface IApplicationPaths
{
    string RootDirectory { get; }
    string DatabasePath { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }

    void EnsureCreated();
}
