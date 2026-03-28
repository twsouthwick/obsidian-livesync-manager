using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace obsidian_sync_manager.Web;

public class CouchDbAdminClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var response = await httpClient.PutAsync($"/{Uri.EscapeDataString(name)}", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteDatabaseAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/{Uri.EscapeDataString(name)}", cancellationToken);
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

    public async Task SetDatabaseSecurityAsync(string db, string username, CancellationToken cancellationToken = default)
    {
        await PutJsonAsync($"/{Uri.EscapeDataString(db)}/_security", new
        {
            admins = new { names = Array.Empty<string>(), roles = Array.Empty<string>() },
            members = new { names = new[] { username }, roles = Array.Empty<string>() }
        }, cancellationToken);
    }

    public async Task<string[]> ListDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("/_all_dbs", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string[]>(JsonOptions, cancellationToken) ?? [];
    }

    public async Task<JsonDocument> GetDatabaseSecurityAsync(string db, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/{Uri.EscapeDataString(db)}/_security", cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task PutConfigAsync(string section, string key, string value, CancellationToken cancellationToken)
    {
        var url = $"/_node/_local/_config/{Uri.EscapeDataString(section)}/{Uri.EscapeDataString(key)}";
        var content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        var response = await httpClient.PutAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PutJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PutAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
