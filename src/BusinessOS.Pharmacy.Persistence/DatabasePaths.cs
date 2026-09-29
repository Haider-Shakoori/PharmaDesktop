namespace BusinessOS.Pharmacy.Persistence;

public static class DatabasePaths
{
    public static string GetBaseDirectory(bool ensureExists = true)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var path = Path.Combine(root, "BusinessOS", "Pharmacy");

        if (ensureExists)
        {
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, "backups"));
            Directory.CreateDirectory(Path.Combine(path, "logs"));
        }

        return path;
    }

    public static string GetDatabasePath(bool ensureDirectory = true) =>
        Path.Combine(GetBaseDirectory(ensureDirectory), "pharmacy.db");
}
