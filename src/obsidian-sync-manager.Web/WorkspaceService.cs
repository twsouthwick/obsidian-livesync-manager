using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace obsidian_sync_manager.Web;

public sealed partial class WorkspaceService(
    CouchDbClient couchDb,
    IOptions<CouchDbOptions> couchDbOptions,
    IUserSecretProvider userSecretProvider)
{
    public static bool IsValidWorkspaceName(string name) =>
        name.Length is > 0 and <= 64 && ValidNameRegex().IsMatch(name);

    public async Task<List<WorkspaceInfo>> ListAsync(string username, CancellationToken cancellationToken = default)
    {
        var docs = await couchDb.ListWorkspaceDocsAsync(cancellationToken);
        return docs
            .Where(d => d.Members.Contains(username, StringComparer.Ordinal))
            .Select(d => new WorkspaceInfo(d.Id, d.Name, d.DatabaseName, d.Members, d.E2eePassphrase))
            .OrderBy(w => w.Name)
            .ToList();
    }

    public async Task<WorkspaceInfo?> GetAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var doc = await couchDb.GetWorkspaceDocAsync(workspaceId, cancellationToken);
        if (doc is null) return null;
        return new WorkspaceInfo(doc.Id, doc.Name, doc.DatabaseName, doc.Members, doc.E2eePassphrase);
    }

    public async Task EnsureCurrentUserAsync(string username, string sub, CancellationToken cancellationToken = default)
    {
        var password = userSecretProvider.DeriveUserPassword(sub);
        await couchDb.EnsureUserAsync(username, password, cancellationToken);
    }

    public async Task<(bool Success, WorkspaceInfo? Workspace)> CreateAsync(
        string username, string name, CancellationToken cancellationToken = default)
    {
        var workspaceId = Guid.NewGuid().ToString("N")[..12];
        var dbName = $"livesync-{workspaceId}";

        if (!await couchDb.CreateDatabaseAsync(dbName, cancellationToken))
            return (false, null);

        await couchDb.SetDatabaseSecurityAsync(dbName, [username], cancellationToken);

        var doc = new WorkspaceRegistryDoc
        {
            Id = workspaceId,
            Name = name,
            DatabaseName = dbName,
            CreatedBy = username,
            Members = [username],
            E2eePassphrase = SetupUriEncryptionService.GenerateE2eePassphrase(),
        };
        await couchDb.PutWorkspaceDocAsync(doc, cancellationToken);

        return (true, new WorkspaceInfo(workspaceId, name, dbName, doc.Members, doc.E2eePassphrase));
    }

    public async Task<bool> DeleteAsync(string workspaceId, string username, CancellationToken cancellationToken = default)
    {
        var doc = await couchDb.GetWorkspaceDocAsync(workspaceId, cancellationToken);
        if (doc is null || !doc.Members.Contains(username))
            return false;

        await couchDb.DeleteDatabaseAsync(doc.DatabaseName, cancellationToken);
        await couchDb.DeleteWorkspaceDocAsync(workspaceId, doc.Rev!, cancellationToken);
        return true;
    }

    public async Task<bool> AddMemberAsync(
        string workspaceId, string requestingUsername, string targetUsername,
        CancellationToken cancellationToken = default)
    {
        var doc = await couchDb.GetWorkspaceDocAsync(workspaceId, cancellationToken);
        if (doc is null || !doc.Members.Contains(requestingUsername))
            return false;

        if (doc.Members.Contains(targetUsername))
            return true; // already a member

        // Target user must have logged in at least once (CouchDB account created on first visit)
        if (!await couchDb.UserExistsAsync(targetUsername, cancellationToken))
            return false;

        doc.Members.Add(targetUsername);
        await couchDb.PutWorkspaceDocAsync(doc, cancellationToken);
        await couchDb.SetDatabaseSecurityAsync(doc.DatabaseName, doc.Members, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(
        string workspaceId, string requestingUsername, string targetUsername,
        CancellationToken cancellationToken = default)
    {
        var doc = await couchDb.GetWorkspaceDocAsync(workspaceId, cancellationToken);
        if (doc is null || !doc.Members.Contains(requestingUsername))
            return false;

        if (!doc.Members.Contains(targetUsername))
            return true; // not a member anyway

        if (doc.Members.Count == 1)
            return false; // can't remove last member

        doc.Members.Remove(targetUsername);
        await couchDb.PutWorkspaceDocAsync(doc, cancellationToken);
        await couchDb.SetDatabaseSecurityAsync(doc.DatabaseName, doc.Members, cancellationToken);
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
