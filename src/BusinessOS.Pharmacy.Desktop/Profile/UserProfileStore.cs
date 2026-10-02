using System.IO;
using System.Text.Json;

namespace BusinessOS.Pharmacy.Desktop.Profile;

public sealed record UserProfile(
    string FirstName,
    string LastName,
    string? ImagePath)
{
    public string DisplayName => string.Join(
        " ",
        new[] { FirstName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public bool HasImage =>
        !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);
}

/// <summary>
/// Local workstation profile (display name and photo) for the signed-in user.
/// </summary>
public sealed class UserProfileStore
{
    private readonly string _folder;
    private readonly string _file;

    public UserProfileStore()
    {
        _folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BusinessOS",
            "Pharmacy");
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "profile.json");
    }

    public event Action<UserProfile>? ProfileChanged;

    public UserProfile Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                return JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(_file))
                       ?? new UserProfile(string.Empty, string.Empty, null);
            }
        }
        catch
        {
            // Corrupt profile files fall back to defaults.
        }

        return new UserProfile(string.Empty, string.Empty, null);
    }

    public void Save(UserProfile profile)
    {
        File.WriteAllText(_file, JsonSerializer.Serialize(profile));
        ProfileChanged?.Invoke(profile);
    }

    public string CopyImage(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var destination = Path.Combine(_folder, "avatar" + extension);
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }
}
