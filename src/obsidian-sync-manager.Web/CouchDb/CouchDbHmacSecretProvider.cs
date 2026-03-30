using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Swick.Obsidian.SyncManager.Web.CouchDb;

namespace Swick.Obsidian.SyncManager.Web;

internal sealed class CouchDbHmacSecretProvider(
    IDataProtectionProvider dataProtectionProvider,
    CouchDbClient couchDb) : IUserSecretProvider
{
    private const string DocId = "app:hmac-secret";
    private const string Purpose = "HmacUserSecret";
    private const int KeyLengthBytes = 32;

    private byte[]? _secretKey;

    private CouchDbDatabase Registry => couchDb.Database("workspace-registry");

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

        var existing = await Registry.GetAsync<AppSecretDoc>(DocId, cancellationToken);
        if (existing?.EncryptedKey is not null)
        {
            _secretKey = protector.Unprotect(Convert.FromBase64String(existing.EncryptedKey));
            return;
        }

        // Generate a new random key and store it encrypted
        var rawKey = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        var protectedKey = protector.Protect(rawKey);
        await Registry.PutAsync(DocId, new AppSecretDoc
        {
            Id = DocId,
            EncryptedKey = Convert.ToBase64String(protectedKey)
        }, cancellationToken);
        _secretKey = rawKey;
    }

    private class AppSecretDoc : CouchDbDocument
    {
        [JsonPropertyName("value")]
        public string? EncryptedKey { get; init; }
    }
}
