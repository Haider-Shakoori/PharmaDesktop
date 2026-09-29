using BusinessOS.Pharmacy.Infrastructure.Storage;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Infrastructure;

public sealed class ApplicationPathsTests
{
    [Fact]
    public void EnsureCreated_CreatesStableDataFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS.Pharmacy.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new ApplicationPaths(root);
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.True(Directory.Exists(paths.BackupsDirectory));
            Assert.True(Directory.Exists(paths.LogsDirectory));
            Assert.Equal(Path.Combine(paths.RootDirectory, "pharmacy.db"), paths.DatabasePath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
