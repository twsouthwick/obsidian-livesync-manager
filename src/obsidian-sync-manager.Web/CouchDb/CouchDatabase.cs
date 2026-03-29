using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Swick.Obsidian.SyncManager.Web.CouchDb;

public class CouchDatabase(HttpClient httpClient, string name)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string DbPath => $"/{Uri.EscapeDataString(name)}";

    public CouchDatabaseSecurity Security => new(httpClient, name);

    public async Task<bool> CreateIfNotExistsAsync(CancellationToken cancellationToken = default)
    {
        using var head = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, DbPath), cancellationToken);
        if (head.IsSuccessStatusCode)
            return true;

        using var response = await httpClient.PutAsync(DbPath, null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(DbPath, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<T?> GetAsync<T>(string id, CancellationToken cancellationToken = default) where T : class
    {
        using var response = await httpClient.GetAsync($"{DbPath}/{Uri.EscapeDataString(id)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task PutAsync<T>(string id, T doc, CancellationToken cancellationToken = default)
    {
        using var content = JsonContent.Create(doc, options: JsonOptions);
        using var response = await httpClient.PutAsync($"{DbPath}/{Uri.EscapeDataString(id)}", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> DeleteDocumentAsync(string id, string rev, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"{DbPath}/{Uri.EscapeDataString(id)}?rev={Uri.EscapeDataString(rev)}",
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async IAsyncEnumerable<T> GetAllAsync<T>([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"{DbPath}/_all_docs?include_docs=true", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.TryGetProperty("id", out var idProp) && idProp.GetString()?.StartsWith('_') == true)
                continue;

            if (row.TryGetProperty("doc", out var docElement))
            {
                var item = docElement.Deserialize<T>(JsonOptions);
                if (item is not null)
                    yield return item;
            }
        }
    }
}

public class CouchDatabaseSecurity(HttpClient httpClient, string name)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<string> GetMembersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/{Uri.EscapeDataString(name)}/_security", cancellationToken);
        if (!response.IsSuccessStatusCode)
            yield break;

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (doc.RootElement.TryGetProperty("members", out var members) &&
            members.TryGetProperty("names", out var names))
        {
            foreach (var n in names.EnumerateArray())
                yield return n.GetString()!;
        }
    }

    public async Task SetAsync(IEnumerable<string> memberNames, CancellationToken cancellationToken = default)
    {
        var names = memberNames.ToArray();
        using var content = JsonContent.Create(new
        {
            admins = new { names, roles = Array.Empty<string>() },
            members = new { names, roles = Array.Empty<string>() }
        }, options: JsonOptions);
        using var response = await httpClient.PutAsync($"/{Uri.EscapeDataString(name)}/_security", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
