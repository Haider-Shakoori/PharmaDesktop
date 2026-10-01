using System.Security.Cryptography;

namespace BusinessOS.Pharmacy.Licensing;

public interface IOfflinePasswordVerifier
{
    OfflinePasswordCredential Create(string password);
    bool Verify(string password, OfflinePasswordCredential credential);
}

public sealed class OfflinePasswordVerifier : IOfflinePasswordVerifier
{
    public const int DefaultIterations = 600_000;
    public const int MinimumIterations = 100_000;
    public const int MaximumIterations = 1_500_000;
    public const int MaximumPasswordLength = 1024;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public OfflinePasswordCredential Create(string password)
    {
        ValidatePassword(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return new OfflinePasswordCredential(
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash),
            DefaultIterations);
    }

    public bool Verify(string password, OfflinePasswordCredential credential)
    {
        if (string.IsNullOrEmpty(password) ||
            password.Length > MaximumPasswordLength ||
            credential.Iterations is < MinimumIterations or > MaximumIterations)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(credential.SaltBase64);
            var expected = Convert.FromBase64String(credential.HashBase64);
            if (salt.Length != SaltSize || expected.Length != HashSize)
            {
                CryptographicOperations.ZeroMemory(expected);
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                credential.Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            try
            {
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
                CryptographicOperations.ZeroMemory(expected);
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length > MaximumPasswordLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(password),
                $"Password length cannot exceed {MaximumPasswordLength} characters.");
        }
    }
}
