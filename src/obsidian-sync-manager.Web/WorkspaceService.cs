using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Swick.Obsidian.SyncManager.Web.CouchDb;

namespace Swick.Obsidian.SyncManager.Web;

public sealed partial class WorkspaceService(
    CouchDbClient couchDb,
    IOptions<CouchDbOptions> couchDbOptions,
    IUserSecretProvider userSecretProvider)
{
    private CouchDatabase Registry => couchDb.Database("workspace-registry");

    public static bool IsValidWorkspaceName(string name) =>
        name.Length is > 0 and <= 64 && ValidNameRegex().IsMatch(name);

    public async IAsyncEnumerable<WorkspaceInfo> GetAllAsync(string username, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var d in Registry.GetAllAsync<WorkspaceRegistryDoc>(cancellationToken))
        {
            if (d.DatabaseName.Length == 0) continue;
            var members = await couchDb.Database(d.DatabaseName).Security.GetMembersAsync(cancellationToken).ToListAsync(cancellationToken);
            if (members.Contains(username, StringComparer.Ordinal))
                yield return new WorkspaceInfo(d.Id, d.Name, d.DatabaseName, members, d.E2eePassphrase);
        }
    }

    public async Task<WorkspaceInfo?> GetAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var doc = await Registry.GetAsync<WorkspaceRegistryDoc>(workspaceId, cancellationToken);
        if (doc is null) return null;
        var members = await couchDb.Database(doc.DatabaseName).Security.GetMembersAsync(cancellationToken).ToListAsync(cancellationToken);
        return new WorkspaceInfo(doc.Id, doc.Name, doc.DatabaseName, members, doc.E2eePassphrase);
    }

    public async Task EnsureCurrentUserAsync(string username, string sub, CancellationToken cancellationToken = default)
    {
        var password = userSecretProvider.DeriveUserPassword(sub);
        await couchDb.Users.CreateIfNotExistsAsync(username, password, cancellationToken);
    }

    public async Task<(bool Success, WorkspaceInfo? Workspace)> CreateAsync(
        string username, string name, CancellationToken cancellationToken = default)
    {
        var workspaceId = Guid.NewGuid().ToString("N")[..12];
        var dbName = $"livesync-{workspaceId}";
        var db = couchDb.Database(dbName);

        if (!await db.CreateIfNotExistsAsync(cancellationToken))
            return (false, null);

        await db.Security.SetAsync([username], cancellationToken);

        var e2eePassphrase = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var doc = new WorkspaceRegistryDoc
        {
            Id = workspaceId,
            Name = name,
            DatabaseName = dbName,
            CreatedBy = username,
            E2eePassphrase = e2eePassphrase,
        };
        await Registry.PutAsync(doc.Id, doc, cancellationToken);

        return (true, new WorkspaceInfo(workspaceId, name, dbName, [username], doc.E2eePassphrase));
    }

    public async Task<bool> DeleteAsync(string workspaceId, string username, CancellationToken cancellationToken = default)
    {
        var doc = await Registry.GetAsync<WorkspaceRegistryDoc>(workspaceId, cancellationToken);
        if (doc is null) return false;

        var db = couchDb.Database(doc.DatabaseName);

        if (!await db.Security.GetMembersAsync(cancellationToken).ContainsAsync(username, cancellationToken: cancellationToken))
            return false;

        await db.DeleteAsync(cancellationToken);
        await Registry.DeleteDocumentAsync(workspaceId, doc.Rev!, cancellationToken);
        
        return true;
    }

    public async Task<bool> AddMemberAsync(
        string workspaceId, string requestingUsername, string targetUsername,
        CancellationToken cancellationToken = default)
    {
        var doc = await Registry.GetAsync<WorkspaceRegistryDoc>(workspaceId, cancellationToken);
        if (doc is null) return false;

        var db = couchDb.Database(doc.DatabaseName);
        var members = await db.Security.GetMembersAsync(cancellationToken).ToListAsync(cancellationToken);

        if (!members.Contains(requestingUsername, StringComparer.Ordinal))
            return false;

        if (members.Contains(targetUsername, StringComparer.Ordinal))
            return true; // already a member

        // Target user must have logged in at least once (CouchDB account created on first visit)
        if (!await couchDb.Users.ExistsAsync(targetUsername, cancellationToken))
            return false;

        members.Add(targetUsername);
        await db.Security.SetAsync(members, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(
        string workspaceId, string requestingUsername, string targetUsername,
        CancellationToken cancellationToken = default)
    {
        var doc = await Registry.GetAsync<WorkspaceRegistryDoc>(workspaceId, cancellationToken);
        if (doc is null) return false;

        var db = couchDb.Database(doc.DatabaseName);
        var members = await db.Security.GetMembersAsync(cancellationToken).ToListAsync(cancellationToken);

        if (!members.Contains(requestingUsername, StringComparer.Ordinal))
            return false;

        if (!members.Contains(targetUsername, StringComparer.Ordinal))
            return true; // not a member anyway

        if (members.Count == 1)
            return false; // can't remove last member

        members.Remove(targetUsername);
        await db.Security.SetAsync(members, cancellationToken);
        return true;
    }

    public EncryptedSetupUri GenerateSetupUri(string username, string sub, string workspaceId, string databaseName, string e2eePassphrase)
    {
        var password = userSecretProvider.DeriveUserPassword(sub);
        var couchDbUrl = couchDbOptions.Value.Url;

        var settings = JsonSerializer.Serialize(new
        {
            couchDB_URI = couchDbUrl,
            couchDB_USER = username,
            couchDB_PASSWORD = password,
            couchDB_DBNAME = databaseName,
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

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex ValidNameRegex();
}

public class WorkspaceRegistryDoc : CouchDocument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("databaseName")]
    public string DatabaseName { get; set; } = "";

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("e2eePassphrase")]
    public string E2eePassphrase { get; set; } = "";
}
