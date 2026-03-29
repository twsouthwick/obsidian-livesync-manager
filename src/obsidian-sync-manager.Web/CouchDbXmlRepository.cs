using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace obsidian_sync_manager.Web;

public sealed class CouchDbXmlRepository(HttpClient httpClient) : IXmlRepository
{
    private const string KeysDb = "data-protection-keys";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        return GetAllElementsAsync().GetAwaiter().GetResult();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        StoreElementAsync(element, friendlyName).GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyCollection<XElement>> GetAllElementsAsync()
    {
        await EnsureDatabaseAsync();

        using var response = await httpClient.GetAsync($"/{KeysDb}/_all_docs?include_docs=true");
        if (!response.IsSuccessStatusCode)
            return [];

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var elements = new List<XElement>();

        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.TryGetProperty("doc", out var docElement) &&
                docElement.TryGetProperty("xml", out var xmlProp))
            {
                var xml = xmlProp.GetString();
                if (xml is not null)
                    elements.Add(XElement.Parse(xml));
            }
        }

        return elements;
    }

    private async Task StoreElementAsync(XElement element, string friendlyName)
    {
        await EnsureDatabaseAsync();

        var docId = Guid.NewGuid().ToString("N");
        var body = new Dictionary<string, string>
        {
            ["_id"] = docId,
            ["xml"] = element.ToString(SaveOptions.DisableFormatting),
            ["friendlyName"] = friendlyName ?? ""
        };

        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await httpClient.PutAsync($"/{KeysDb}/{docId}", content);
        response.EnsureSuccessStatusCode();
    }

    private async Task EnsureDatabaseAsync()
    {
        using var head = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/{KeysDb}"));
        if (head.IsSuccessStatusCode)
            return;

        using var response = await httpClient.PutAsync($"/{KeysDb}", null);
        // 412 = already exists (race condition), that's fine
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PreconditionFailed)
            response.EnsureSuccessStatusCode();
    }
}
