using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Aspire.Hosting;

public static class DataProtectionExtensions
{
    private static readonly string DevCertPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "obsidian-sync-manager", "data-protection-dev.pfx");

    private const string DevCertPassword = "dev-only";

    public static IResourceBuilder<T> WithDataProtectionDevCertificate<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment
    {
        EnsureDevCertificateExists();

        return builder
            .WithEnvironment("DataProtection__CertificatePath", DevCertPath)
            .WithEnvironment("DataProtection__CertificatePassword", DevCertPassword);
    }

    private static void EnsureDevCertificateExists()
    {
        if (File.Exists(DevCertPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(DevCertPath)!);
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=obsidian-sync-manager-dp-dev", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(5));
        File.WriteAllBytes(DevCertPath, cert.Export(X509ContentType.Pfx, DevCertPassword));
    }
}
