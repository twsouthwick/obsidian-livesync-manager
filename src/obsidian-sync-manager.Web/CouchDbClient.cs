using System.Text.Json;
using System.Text.Json.Serialization;

namespace obsidian_sync_manager.Web;

public class CouchDbClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string RegistryDb = "workspace-registry";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Enable single-node setup
        await PostJsonAsync("/_cluster_setup", new
        {
            action = "enable_single_node",
            bind_address = "0.0.0.0",
            port = 5984,
            singlenode = true
        }, cancellationToken);

        // Require valid user for all requests
        await PutConfigAsync("chttpd", "require_valid_user", "true", cancellationToken);
        await PutConfigAsync("chttpd_auth", "require_valid_user", "true", cancellationToken);

        // Enable CORS for Obsidian LiveSync clients
        await PutConfigAsync("httpd", "enable_cors", "true", cancellationToken);
        await PutConfigAsync("cors", "origins", "app://obsidian.md,capacitor://localhost,http://localhost", cancellationToken);
        await PutConfigAsync("cors", "credentials", "true", cancellationToken);
        await PutConfigAsync("cors", "headers", "accept, authorization, content-type, origin, referer", cancellationToken);
        await PutConfigAsync("cors", "methods", "GET, PUT, POST, HEAD, DELETE", cancellationToken);

        // Set max HTTP request size (4 GB) and max document size (50 MB)
        await PutConfigAsync("chttpd", "max_http_request_size", "4294967296", cancellationToken);
        await PutConfigAsync("couchdb", "max_document_size", "50000000", cancellationToken);
    }

    public async Task<bool> CreateDatabaseAsync(string name, CancellationToken cancellationToken = default)
    {
        var escaped = Uri.EscapeDataString(name);
        using var head = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/{escaped}"), cancellationToken);
        if (head.IsSuccessStatusCode)
            return true; // already exists

        using var response = await httpClient.PutAsync($"/{escaped}", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteDatabaseAsync(string name, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/{Uri.EscapeDataString(name)}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task CreateUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var userId = $"org.couchdb.user:{username}";
        await PutJsonAsync($"/_users/{Uri.EscapeDataString(userId)}", new
        {
            _id = userId,
            name = username,
            password,
            type = "user",
            roles = Array.Empty<string>()
        }, cancellationToken);
    }

    public async Task EnsureUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var userId = $"org.couchdb.user:{username}";
        var encodedId = Uri.EscapeDataString(userId);
        var url = $"/_users/{encodedId}";

        // Retry on 409 Conflict (concurrent rev update)
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Get _rev if user already exists (needed for update)
            string? rev = null;
            using var getResponse = await httpClient.GetAsync(url, cancellationToken);
            if (getResponse.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(
                    await getResponse.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
                rev = doc.RootElement.GetProperty("_rev").GetString();
            }

            var userDoc = new Dictionary<string, object>
            {
                ["_id"] = userId,
                ["name"] = username,
                ["password"] = password,
                ["type"] = "user",
                ["roles"] = Array.Empty<string>()
            };
            if (rev is not null)
                userDoc["_rev"] = rev;

            using var content = JsonContent.Create(userDoc, options: JsonOptions);
            using var response = await httpClient.PutAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Created)
                return;

            if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
                response.EnsureSuccessStatusCode();
        }

        // Final attempt — let it throw on failure
        await PutJsonAsync(url, new Dictionary<string, object>
        {
            ["_id"] = userId,
            ["name"] = username,
            ["password"] = password,
            ["type"] = "user",
            ["roles"] = Array.Empty<string>()
        }, cancellationToken);
    }

    public async Task SetDatabaseSecurityAsync(string db, IEnumerable<string> memberNames, CancellationToken cancellationToken = default)
    {
        var names = memberNames.ToArray();
        await PutJsonAsync($"/{Uri.EscapeDataString(db)}/_security", new
        {
            admins = new { names, roles = Array.Empty<string>() },
            members = new { names, roles = Array.Empty<string>() }
        }, cancellationToken);
    }

    public async Task<string[]> ListDatabasesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/_all_dbs", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string[]>(JsonOptions, cancellationToken) ?? [];
    }

    public async Task<JsonDocument> GetDatabaseSecurityAsync(string db, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/{Uri.EscapeDataString(db)}/_security", cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task<bool> UserExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        var userId = $"org.couchdb.user:{username}";
        using var response = await httpClient.GetAsync($"/_users/{Uri.EscapeDataString(userId)}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    // --- Workspace registry ---

    public async Task CreateRegistryDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await CreateDatabaseAsync(RegistryDb, cancellationToken);
    }

    public async Task<WorkspaceRegistryDoc?> GetWorkspaceDocAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/{RegistryDb}/{Uri.EscapeDataString(workspaceId)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<WorkspaceRegistryDoc>(JsonOptions, cancellationToken);
    }

    public async Task PutWorkspaceDocAsync(WorkspaceRegistryDoc doc, CancellationToken cancellationToken = default)
    {
        await PutJsonAsync($"/{RegistryDb}/{Uri.EscapeDataString(doc.Id)}", doc, cancellationToken);
    }

    public async Task<bool> DeleteWorkspaceDocAsync(string workspaceId, string rev, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"/{RegistryDb}/{Uri.EscapeDataString(workspaceId)}?rev={Uri.EscapeDataString(rev)}",
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<WorkspaceRegistryDoc>> ListWorkspaceDocsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/{RegistryDb}/_all_docs?include_docs=true", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var results = new List<WorkspaceRegistryDoc>();
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.TryGetProperty("doc", out var docElement))
            {
                var workspace = docElement.Deserialize<WorkspaceRegistryDoc>(JsonOptions);
                if (workspace is not null && !workspace.Id.StartsWith('_'))
                    results.Add(workspace);
            }
        }
        return results;
    }

    // --- App secrets (singleton docs in workspace-registry) ---

    public async Task<string?> GetAppSecretAsync(string docId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/{RegistryDb}/{Uri.EscapeDataString(docId)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return doc.RootElement.TryGetProperty("encryptedKey", out var prop)
            ? prop.GetString()
            : null;
    }

    public async Task PutAppSecretAsync(string docId, string encryptedKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, string>
        {
            ["_id"] = docId,
            ["encryptedKey"] = encryptedKey
        };
        await PutJsonAsync($"/{RegistryDb}/{Uri.EscapeDataString(docId)}", body, cancellationToken);
    }

    // --- Internal helpers ---

    private async Task PutConfigAsync(string section, string key, string value, CancellationToken cancellationToken)
    {
        var url = $"/_node/_local/_config/{Uri.EscapeDataString(section)}/{Uri.EscapeDataString(key)}";
        using var content = JsonContent.Create(value);
        using var response = await httpClient.PutAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PutJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await httpClient.PutAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public class WorkspaceRegistryDoc
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("_rev")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rev { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("databaseName")]
    public string DatabaseName { get; set; } = "";

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("members")]
    public List<string> Members { get; set; } = [];

    [JsonPropertyName("e2eePassphrase")]
    public string E2eePassphrase { get; set; } = "";
}
