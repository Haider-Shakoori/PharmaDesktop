using BusinessOS.Pharmacy.Licensing;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Authentication;

public sealed class OfflinePasswordVerifierTests
{
    [Fact]
    public void Created_verifier_accepts_correct_password_and_rejects_wrong_password()
    {
        var verifier = new OfflinePasswordVerifier();
        var credential = verifier.Create("correct-horse-battery-staple");

        Assert.True(verifier.Verify("correct-horse-battery-staple", credential));
        Assert.False(verifier.Verify("wrong-password", credential));
        Assert.Equal(OfflinePasswordVerifier.DefaultIterations, credential.Iterations);
    }

    [Fact]
    public void Same_password_produces_unique_salts_and_hashes()
    {
        var verifier = new OfflinePasswordVerifier();

        var first = verifier.Create("same-password");
        var second = verifier.Create("same-password");

        Assert.NotEqual(first.SaltBase64, second.SaltBase64);
        Assert.NotEqual(first.HashBase64, second.HashBase64);
    }
    [Fact]
    public void Oversized_password_and_corrupt_credential_are_rejected_without_expensive_work()
    {
        var verifier = new OfflinePasswordVerifier();
        var oversized = new string('x', OfflinePasswordVerifier.MaximumPasswordLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => verifier.Create(oversized));
        Assert.False(verifier.Verify(
            "password",
            new OfflinePasswordCredential(
                Convert.ToBase64String(new byte[16]),
                Convert.ToBase64String(new byte[31]),
                OfflinePasswordVerifier.DefaultIterations)));
        Assert.False(verifier.Verify(
            "password",
            new OfflinePasswordCredential(
                Convert.ToBase64String(new byte[16]),
                Convert.ToBase64String(new byte[32]),
                OfflinePasswordVerifier.MaximumIterations + 1)));
    }

}
