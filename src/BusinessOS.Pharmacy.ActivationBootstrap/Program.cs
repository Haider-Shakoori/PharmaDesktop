using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Licensing;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;
using BusinessOS.Pharmacy.Application.Abstractions.Time;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Infrastructure.Time;
using BusinessOS.Pharmacy.Licensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.ActivationBootstrap;

internal static class Program
{
    private const string LicensingBaseUrl = "https://pharmacy.darmaltoon.com";
    private const string SigningPublicKey = "gtxLpVaUH/6OJ2CfSw2z+8hmUE6vu18Veb5q0IuWH1Y=";

    public static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
        var resultFile = ReadOption(args, "--result-file");

        try
        {
            using var services = BuildServices();

            return command switch
            {
                "verify" => await VerifyExistingActivationAsync(services, resultFile),
                "activate" => await ActivateAsync(services, args, resultFile),
                _ => Fail(resultFile, "Unsupported activation-bootstrap command.", 64),
            };
        }
        catch (LicenseApiException exception)
        {
            return Fail(resultFile, exception.Message, exception.IsRetryable ? 75 : 2);
        }
        catch (Exception exception)
        {
            return Fail(
                resultFile,
                "Darmaltoon could not verify this Windows activation. " + exception.Message,
                3);
        }
    }

    private static ServiceProvider BuildServices()
    {
        var values = new Dictionary<string, string?>
        {
            [$"{LicenseApiOptions.SectionName}:BaseUrl"] = LicensingBaseUrl,
            [$"{LicenseApiOptions.SectionName}:ActivationPath"] = "/api/v1/license/activate",
            [$"{LicenseApiOptions.SectionName}:RefreshPath"] = "/api/v1/desktop/license/refresh",
            [$"{LicenseApiOptions.SectionName}:SessionLoginPath"] = "/api/v1/desktop/session/login",
            [$"{LicenseApiOptions.SectionName}:SessionRefreshPath"] = "/api/v1/desktop/session/refresh",
            [$"{LicenseApiOptions.SectionName}:SigningPublicKey"] = SigningPublicKey,
            [$"{LicenseApiOptions.SectionName}:TimeoutSeconds"] = "15",
            [$"{LicenseApiOptions.SectionName}:ClockRollbackToleranceMinutes"] = "10",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(_ => new ApplicationPaths());
        services.AddSingleton<IClock, SystemClock>();
        services.AddBusinessOSLicensing(configuration);

        return services.BuildServiceProvider();
    }

    private static async Task<int> VerifyExistingActivationAsync(
        IServiceProvider services,
        string? resultFile)
    {
        var store = services.GetRequiredService<IActivationStore>();
        var verifier = services.GetRequiredService<ISignedLeaseVerifier>();
        var identity = services.GetRequiredService<IInstallationIdentityProvider>();

        var state = await store.LoadAsync();
        if (state is null)
        {
            return Fail(resultFile, "No existing Darmaltoon Windows activation was found.", 4);
        }

        var entitlement = verifier.Verify(state.LeaseToken);
        var installationId = await identity.GetOrCreateAsync();

        if (!string.Equals(state.DeviceId, installationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entitlement.DeviceId, installationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entitlement.ActivationId, state.Entitlement.ActivationId, StringComparison.Ordinal))
        {
            return Fail(
                resultFile,
                "The saved Darmaltoon activation belongs to another Windows installation.",
                5);
        }

        WriteResult(
            resultFile,
            "valid",
            $"Existing activation verified for plan {entitlement.PlanCode}.");

        return 0;
    }

    private static async Task<int> ActivateAsync(
        IServiceProvider services,
        string[] args,
        string? resultFile)
    {
        var licenseFile = ReadOption(args, "--license-file");
        if (string.IsNullOrWhiteSpace(licenseFile) || !File.Exists(licenseFile))
        {
            return Fail(resultFile, "The installer did not provide a license key.", 64);
        }

        string licenseKey;
        try
        {
            licenseKey = (await File.ReadAllTextAsync(licenseFile)).Trim();
        }
        finally
        {
            try
            {
                File.Delete(licenseFile);
            }
            catch
            {
            }
        }

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return Fail(resultFile, "Enter the one-time Windows activation key provided by Darmaltoon support.", 2);
        }

        var licensing = services.GetRequiredService<ILicenseService>();
        var entitlement = await licensing.ActivateAsync(licenseKey);

        WriteResult(
            resultFile,
            "activated",
            $"License accepted and bound to this PC. Plan: {entitlement.PlanCode}.");

        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int Fail(string? resultFile, string message, int exitCode)
    {
        WriteResult(resultFile, "error", message);
        Console.Error.WriteLine(message);
        return exitCode;
    }

    private static void WriteResult(string? resultFile, string status, string message)
    {
        if (string.IsNullOrWhiteSpace(resultFile))
        {
            return;
        }

        var directory = Path.GetDirectoryName(resultFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            resultFile,
            JsonSerializer.Serialize(new
            {
                status,
                message,
            }));
    }
}
