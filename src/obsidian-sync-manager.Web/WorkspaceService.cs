using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Swick.Obsidian.SyncManager.Web.CouchDb;

namespace Swick.Obsidian.SyncManager.Web;

public sealed partial class WorkspaceService(
    CouchDbClient couchDb,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<CouchDbOptions> couchDbOptions,
    IUserSecretProvider userSecretProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("WorkspaceService");

    private CouchDbDatabase Registry => couchDb.Database("workspace-registry");

    public static bool IsValidWorkspaceName(string name) =>
        name.Length is > 0 and <= 64 && ValidNameRegex().IsMatch(name);

    public async IAsyncEnumerable<WorkspaceInfo> GetAllAsync(string username, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var d in Registry.GetAllAsync<WorkspaceRegistryDoc>(cancellationToken))
        {
            if (!string.IsNullOrEmpty(d.DatabaseName))
            {
                var info = await GetWorkspaceInfoAsync(d, cancellationToken);

                if (info.Members.IsMember(username))
                {
                    yield return info;
                }
            }
        }
    }

    public async Task<WorkspaceInfo?> GetAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var doc = await Registry.GetAsync<WorkspaceRegistryDoc>(workspaceId, cancellationToken);

        if (doc is null)
        {
            return null;
        }

        return await GetWorkspaceInfoAsync(doc, cancellationToken);
    }

    private async Task<WorkspaceInfo> GetWorkspaceInfoAsync(WorkspaceRegistryDoc doc, CancellationToken cancellationToken)
    {
        var members = await couchDb.Database(doc.DatabaseName).Security.GetAsync(cancellationToken);
        var decrypted = _protector.CreateProtector(doc.Id).Unprotect(doc.Passphrase);

        return new WorkspaceInfo(doc.Id, doc.Name, doc.DatabaseName, members, decrypted);
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

        var securityRecord = new CouchDbSecurityRecord(
                    new UserRecord([username], []),
                    new UserRecord([], [])
                );
        await db.Security.SetAsync(securityRecord, cancellationToken);

        var e2eePassphrase = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var encryptedPassphrase = _protector.CreateProtector(workspaceId).Protect(e2eePassphrase);

        var doc = new WorkspaceRegistryDoc
        {
            Id = workspaceId,
            Name = name,
            DatabaseName = dbName,
            CreatedBy = username,
            Passphrase = encryptedPassphrase,
        };
        await Registry.PutAsync(doc.Id, doc, cancellationToken);

        return (true, new WorkspaceInfo(workspaceId, name, dbName, securityRecord, doc.Passphrase));
    }

    public async Task<bool> DeleteAsync(string workspaceId, string username, CancellationToken cancellationToken = default)
    {
        var doc = await Registry.GetAsync<WorkspaceRegistryDoc>(workspaceId, cancellationToken);
        if (doc is null) return false;

        var db = couchDb.Database(doc.DatabaseName);

        var members = await db.Security.GetAsync(cancellationToken);

        if (!members.IsAdmin(username))
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
        var securityRecord = await db.Security.GetAsync(cancellationToken);

        if (!securityRecord.IsAdmin(requestingUsername))
            return false;

        if (securityRecord.IsMember(targetUsername))
            return true; // already a member

        // Target user must have logged in at least once (CouchDB account created on first visit)
        if (!await couchDb.Users.ExistsAsync(targetUsername, cancellationToken))
            return false;

        securityRecord = securityRecord with
        {
            Members = securityRecord.Members with { Names = securityRecord.Members.Names.Add(targetUsername) }
        };

        await db.Security.SetAsync(securityRecord, cancellationToken);

        return true;
    }

    public async Task<bool> RemoveMemberAsync(
        string workspaceId, string requestingUsername, string targetUsername,
        CancellationToken cancellationToken = default)
    {
        var doc = await Registry.GetAsync<WorkspaceRegistryDoc>(workspaceId, cancellationToken);
        if (doc is null) return false;

        var db = couchDb.Database(doc.DatabaseName);
        var securityRecord = await db.Security.GetAsync(cancellationToken);

        if (!securityRecord.IsAdmin(requestingUsername))
            return false;

        if (!securityRecord.IsMember(targetUsername))
            return true;

        var updatedSecurityRecord = securityRecord with
        {
            Members = securityRecord.Members with { Names = securityRecord.Members.Names.Remove(targetUsername) },
            Admins = securityRecord.Admins with { Names = securityRecord.Admins.Names.Remove(targetUsername) }
        };

        if (securityRecord.Admins.Names.Count == 0)
            return false; // can't remove last admin

        await db.Security.SetAsync(updatedSecurityRecord, cancellationToken);

        return true;
    }

    public EncryptedSetupUri GenerateSetupUri(string username, string sub, string workspaceId, string databaseName, string e2eePassphrase)
    {
        var password = userSecretProvider.DeriveUserPassword(sub);
        var couchDbUrl = couchDbOptions.Value.ExternalUrl ?? couchDbOptions.Value.Url;

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


    private sealed class WorkspaceRegistryDoc : CouchDbDocument
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("databaseName")]
        public string DatabaseName { get; set; } = "";

        [JsonPropertyName("createdBy")]
        public string CreatedBy { get; set; } = "";

        [JsonPropertyName("passphrase")]
        public string Passphrase { get; set; } = "";
    }
}