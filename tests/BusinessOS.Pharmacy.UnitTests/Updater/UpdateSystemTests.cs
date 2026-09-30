using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Updater;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Updater;

public sealed class UpdateSystemTests
{
    [Fact]
    public void Signed_manifest_accepts_valid_signature_and_rejects_tampering()
    {
        using var rsa = RSA.Create(2048);
        var manifest = Sign(
            rsa,
            NewManifest(
                version: "1.1.0",
                minimumSupported: "1.0.0",
                minimumServer: null,
                packageHash: new string('A', 64)));

        UpdateManifestSecurity.ValidateAndVerify(
            manifest,
            rsa.ExportSubjectPublicKeyInfoPem());

        Assert.Throws<InvalidOperationException>(() =>
            UpdateManifestSecurity.ValidateAndVerify(
                manifest with { Version = "1.2.0" },
                rsa.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public async Task Client_update_is_blocked_until_main_server_is_compatible()
    {
        using var rsa = RSA.Create(2048);
        var manifest = Sign(
            rsa,
            NewManifest(
                version: "2.1.0",
                minimumSupported: "1.0.0",
                minimumServer: "2.0.0",
                packageHash: new string('B', 64)));

        using var http = new HttpClient(
            new StaticHandler(() => JsonResponse(manifest)));

        var root = Temp();
        try
        {
            var service = new UpdateService(
                http,
                Options(rsa, "https://updates.example.test/manifest"),
                root);
            var blocked = await service.CheckAsync(
                new Version(1, 5, 0),
                "Client",
                new Version(1, 9, 9));

            Assert.Equal(UpdateAvailability.Blocked, blocked.Availability);

            var allowed = await service.CheckAsync(
                new Version(1, 5, 0),
                "Client",
                new Version(2, 0, 0));

            Assert.Equal(UpdateAvailability.Available, allowed.Availability);
            Assert.Equal(new Version(2, 1, 0), allowed.AvailableVersion);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Download_requires_checksum_and_stages_only_application_files()
    {
        using var rsa = RSA.Create(2048);
        var package = CreateZip(
            ("Darmaltoon.dll", "new-binary"),
            ("BusinessOS.Pharmacy.Domain.dll", "new-domain"));
        var hash = Convert.ToHexString(SHA256.HashData(package));
        var manifest = Sign(
            rsa,
            NewManifest("1.1.0", "1.0.0", null, hash));

        using var http = new HttpClient(new RouteHandler(
            manifest,
            package));

        var root = Temp();
        try
        {
            var service = new UpdateService(
                http,
                Options(rsa, "https://updates.example.test/manifest"),
                root);

            var prepared = await service.DownloadAndStageAsync(manifest);

            Assert.Equal(
                "new-binary",
                await File.ReadAllTextAsync(
                    Path.Combine(prepared.StagingDirectory, "Darmaltoon.dll")));
            Assert.True(File.Exists(prepared.PackagePath));
        }
        finally
        {
            Delete(root);
        }
    }
    [Theory]
    [InlineData("Standalone", true)]
    [InlineData("Server", true)]
    [InlineData("Client", false)]
    public async Task Apply_replaces_only_installation_files_and_preserves_pharmacy_data(
        string deploymentMode,
        bool requiresSafetyBackup)
    {
        var root = Temp();
        var install = Path.Combine(root, "install");
        var data = Path.Combine(root, "program-data");
        var staging = Path.Combine(root, "staging");
        var rollback = Path.Combine(root, "rollback");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(Path.Combine(data, "Config"));
        Directory.CreateDirectory(Path.Combine(data, "licensing"));
        Directory.CreateDirectory(Path.Combine(data, "backups"));

        await File.WriteAllTextAsync(Path.Combine(install, "Darmaltoon.dll"), "old");
        await File.WriteAllTextAsync(Path.Combine(staging, "Darmaltoon.dll"), "new");
        await File.WriteAllTextAsync(Path.Combine(staging, "new-module.dll"), "module");
        await File.WriteAllTextAsync(Path.Combine(data, "pharmacy.db"), "database");
        await File.WriteAllTextAsync(Path.Combine(data, "Config", "network.json"), "network");
        await File.WriteAllTextAsync(Path.Combine(data, "licensing", "activation.bin"), "license");
        var safety = requiresSafetyBackup
            ? Path.Combine(data, "backups", "pre-update.db")
            : null;
        if (safety is not null)
            await File.WriteAllTextAsync(safety, "backup");

        var planPath = Path.Combine(root, "plan.json");
        await WritePlanAsync(
            planPath,
            install,
            data,
            staging,
            rollback,
            deploymentMode,
            safety);

        try
        {
            var result = await UpdateApplier.ApplyAsync(planPath);

            Assert.True(result.Success);
            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(install, "Darmaltoon.dll")));
            Assert.Equal("module", await File.ReadAllTextAsync(Path.Combine(install, "new-module.dll")));
            Assert.Equal("database", await File.ReadAllTextAsync(Path.Combine(data, "pharmacy.db")));
            Assert.Equal("network", await File.ReadAllTextAsync(Path.Combine(data, "Config", "network.json")));
            Assert.Equal("license", await File.ReadAllTextAsync(Path.Combine(data, "licensing", "activation.bin")));
            if (safety is not null)
                Assert.Equal("backup", await File.ReadAllTextAsync(safety));
        }
        finally
        {
            Delete(root);
        }
    }
    [Fact]
    public async Task Apply_rolls_back_files_if_staged_update_contains_protected_data_path()
    {
        var root = Temp();
        var install = Path.Combine(root, "install");
        var data = Path.Combine(root, "program-data");
        var staging = Path.Combine(root, "staging");
        var rollback = Path.Combine(root, "rollback");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.Combine(staging, "config"));
        Directory.CreateDirectory(Path.Combine(data, "backups"));

        var installed = Path.Combine(install, "a.dll");
        await File.WriteAllTextAsync(installed, "old");
        await File.WriteAllTextAsync(Path.Combine(staging, "a.dll"), "new");
        await File.WriteAllTextAsync(Path.Combine(staging, "config", "network.json"), "malicious");
        var safety = Path.Combine(data, "backups", "pre-update.db");
        await File.WriteAllTextAsync(safety, "backup");

        var planPath = Path.Combine(root, "plan.json");
        await WritePlanAsync(
            planPath,
            install,
            data,
            staging,
            rollback,
            "Standalone",
            safety);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                UpdateApplier.ApplyAsync(planPath));

            Assert.Equal("old", await File.ReadAllTextAsync(installed));
            Assert.False(File.Exists(Path.Combine(install, "config", "network.json")));
        }
        finally
        {
            Delete(root);
        }
    }

    private static async Task WritePlanAsync(
        string planPath,
        string install,
        string data,
        string staging,
        string rollback,
        string deploymentMode,
        string? safety)
    {
        var plan = new UpdateApplyPlan(
            1,
            "1.1.0",
            install,
            data,
            staging,
            rollback,
            deploymentMode,
            0,
            null,
            null,
            safety,
            DateTimeOffset.UtcNow);

        await File.WriteAllTextAsync(
            planPath,
            JsonSerializer.Serialize(
                plan,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    private static UpdateManifest NewManifest(
        string version,
        string minimumSupported,
        string? minimumServer,
        string packageHash) =>
        new(
            1,
            "Stable",
            version,
            "v1",
            minimumSupported,
            minimumServer,
            "https://updates.example.test/darmaltoon.zip",
            packageHash,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            "Release notes",
            string.Empty);

    private static UpdateManifest Sign(RSA rsa, UpdateManifest unsigned)
    {
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(
                UpdateManifestSecurity.CanonicalPayload(unsigned)),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        return unsigned with
        {
            Signature = Convert.ToBase64String(signature),
        };
    }

    private static UpdateOptions Options(RSA rsa, string manifestUrl) =>
        new(
            manifestUrl,
            rsa.ExportSubjectPublicKeyInfoPem(),
            30,
            UpdateChannel.Stable,
            10 * 1024 * 1024);

    private static HttpResponseMessage JsonResponse(UpdateManifest manifest) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8,
                "application/json"),
        };

    private static byte[] CreateZip(params (string Path, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(file.Content);
            }
        }

        return memory.ToArray();
    }
    private static string Temp() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-update-tests",
            Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class StaticHandler(Func<HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory());
    }

    private sealed class RouteHandler(
        UpdateManifest manifest,
        byte[] package)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(
                    "darmaltoon.zip",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(package),
                    });
            }

            return Task.FromResult(JsonResponse(manifest));
        }
    }
}
