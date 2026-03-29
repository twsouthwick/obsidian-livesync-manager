using System.Text.Json;

namespace Swick.Obsidian.SyncManager.Web.CouchDb;

public class CouchDbClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CouchDbUsers Users => new(this);

    public CouchDbDatabase Database(string name) => new(httpClient, name);

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

    public async Task<string[]> ListDatabasesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/_all_dbs", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<string[]>(JsonOptions, cancellationToken) ?? [];
    }

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
}
