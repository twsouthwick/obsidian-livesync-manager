using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace obsidian_sync_manager.Web;

public sealed partial class WorkspaceService(CouchDbAdminClient couchDb, IConfiguration config)
{
    public static bool IsValidWorkspaceName(string name) =>
        name.Length is > 0 and <= 64 && ValidNameRegex().IsMatch(name);

    public string GetDatabaseName(string userId, string workspaceName) =>
        $"livesync-{userId}-{workspaceName}";

    public string GetCouchDbUsername(string userId) =>
        $"livesync-{userId}";

    public async Task<List<WorkspaceInfo>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var prefix = $"livesync-{userId}-";
        var databases = await couchDb.ListDatabasesAsync(cancellationToken);

        return databases
            .Where(db => db.StartsWith(prefix, StringComparison.Ordinal))
            .Select(db => new WorkspaceInfo(db[prefix.Length..], db))
            .OrderBy(w => w.Name)
            .ToList();
    }

    public async Task<(bool Success, WorkspaceInfo? Workspace)> CreateAsync(string userId, string name, CancellationToken cancellationToken = default)
    {
        var dbName = GetDatabaseName(userId, name);
        var username = GetCouchDbUsername(userId);
        var password = DeriveUserPassword(userId);

        await couchDb.EnsureUserAsync(username, password, cancellationToken);

        if (!await couchDb.CreateDatabaseAsync(dbName, cancellationToken))
            return (false, null);

        await couchDb.SetDatabaseSecurityAsync(dbName, username, cancellationToken);
        return (true, new WorkspaceInfo(name, dbName));
    }

    public Task<bool> DeleteAsync(string userId, string name, CancellationToken cancellationToken = default) =>
        couchDb.DeleteDatabaseAsync(GetDatabaseName(userId, name), cancellationToken);

    public async Task<bool> ExistsAsync(string userId, string name, CancellationToken cancellationToken = default)
    {
        var dbName = GetDatabaseName(userId, name);
        var databases = await couchDb.ListDatabasesAsync(cancellationToken);
        return databases.Contains(dbName);
    }

    public EncryptedSetupUri GenerateSetupUri(string userId, string workspaceName)
    {
        var dbName = GetDatabaseName(userId, workspaceName);
        var username = GetCouchDbUsername(userId);
        var password = DeriveUserPassword(userId);
        var e2eePassphrase = SetupUriEncryptionService.GenerateE2eePassphrase();
        var couchDbUrl = config["COUCHDB_URL"]
            ?? throw new InvalidOperationException("COUCHDB_URL is not configured.");

        var settings = JsonSerializer.Serialize(new
        {
            couchDB_URI = couchDbUrl,
            couchDB_USER = username,
            couchDB_PASSWORD = password,
            couchDB_DBNAME = dbName,
            encrypt = true,
            passphrase = e2eePassphrase,
            usePathObfuscation = true,
            syncOnStart = true,
            periodicReplication = true,
            syncOnFileOpen = true,
            batchSave = true,
            batch_size = 50,
            batches_limit = 50,
            useHistory = true,
            disableRequestURI = true,
            customChunkSize = 50,
            syncAfterMerge = false,
            concurrencyOfReadChunksOnline = 100,
            minimumIntervalOfReadChunksOnline = 100,
            handleFilenameCaseSensitive = false,
            doNotUseFixedRevisionForChunks = false,
            settingVersion = 10,
        });

        var result = SetupUriEncryptionService.CreateEncryptedSetupUri(settings);
        return result with { E2eePassphrase = e2eePassphrase };
    }

    private string DeriveUserPassword(string userId)
    {
        var secret = config["COUCHDB_USER_SECRET"]
            ?? throw new InvalidOperationException("COUCHDB_USER_SECRET is not configured.");
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(userId));
        return Convert.ToHexStringLower(hash);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex ValidNameRegex();
}
