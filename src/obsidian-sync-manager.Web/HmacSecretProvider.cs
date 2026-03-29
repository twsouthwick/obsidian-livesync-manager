using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace obsidian_sync_manager.Web;

public interface IUserSecretProvider
{
    string DeriveUserPassword(string sub);
}

public sealed class HmacSecretProvider(
    IDataProtectionProvider dataProtectionProvider,
    CouchDbClient couchDb) : IUserSecretProvider
{
    private const string DocId = "app:hmac-secret";
    private const string Purpose = "HmacUserSecret";
    private const int KeyLengthBytes = 32;

    private byte[]? _secretKey;

    public string DeriveUserPassword(string sub)
    {
        var key = _secretKey ?? throw new InvalidOperationException(
            "HMAC secret has not been initialized. Ensure InitializeAsync is called during startup.");
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(sub));
        return Convert.ToHexStringLower(hash);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var protector = dataProtectionProvider.CreateProtector(Purpose);

        var existing = await couchDb.GetAppSecretAsync(DocId, cancellationToken);
        if (existing is not null)
        {
            _secretKey = protector.Unprotect(Convert.FromBase64String(existing));
            return;
        }

        // Generate a new random key and store it encrypted
        var rawKey = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        var protectedKey = protector.Protect(rawKey);
        await couchDb.PutAppSecretAsync(DocId, Convert.ToBase64String(protectedKey), cancellationToken);
        _secretKey = rawKey;
    }
}
