using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BusinessOS.Pharmacy.Application.Abstractions.Networking;
using BusinessOS.Pharmacy.Application.Abstractions.Storage;

namespace BusinessOS.Pharmacy.LocalServer.Security;

public sealed class LocalServerCertificateProvider
{
    private readonly IApplicationPaths _paths;
    private readonly INetworkSecretStore _secrets;

    public LocalServerCertificateProvider(
        IApplicationPaths paths,
        INetworkSecretStore secrets)
    {
        _paths = paths;
        _secrets = secrets;
    }

    public async Task<X509Certificate2> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();

        var password = await _secrets.LoadServerCertificatePasswordAsync(cancellationToken);

        if (File.Exists(_paths.ServerCertificatePath) &&
            !string.IsNullOrWhiteSpace(password))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                _paths.ServerCertificatePath,
                password,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);
        }

        password = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN={Environment.MachineName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);

        foreach (var address in GetPrivateAddresses())
        {
            san.AddIpAddress(address);
        }

        request.CertificateExtensions.Add(san.Build());

        var now = DateTimeOffset.UtcNow;
        using var generated = request.CreateSelfSigned(
            now.AddMinutes(-5),
            now.AddYears(5));

        var pfx = generated.Export(X509ContentType.Pfx, password);
        var temporary = _paths.ServerCertificatePath + ".tmp";
        await File.WriteAllBytesAsync(temporary, pfx, cancellationToken);
        File.Move(temporary, _paths.ServerCertificatePath, overwrite: true);
        await _secrets.SaveServerCertificatePasswordAsync(password, cancellationToken);

        CryptographicOperations.ZeroMemory(pfx);

        return X509CertificateLoader.LoadPkcs12FromFile(
            _paths.ServerCertificatePath,
            password,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);
    }

    public static string Sha256Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

    private static IEnumerable<IPAddress> GetPrivateAddresses()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address))
                {
                    yield return address;
                }
            }
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
