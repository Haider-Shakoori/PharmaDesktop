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
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public OfflinePasswordCredential Create(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

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
            credential.Iterations < 100_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(credential.SaltBase64);
            var expected = Convert.FromBase64String(credential.HashBase64);
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
}
