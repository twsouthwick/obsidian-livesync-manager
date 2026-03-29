using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Swick.Obsidian.SyncManager.Web.CouchDb;

namespace Swick.Obsidian.SyncManager.Web;

public sealed class CouchDbXmlRepository(CouchDbClient couchDb) : IXmlRepository
{
    private CouchDatabase KeysDb => couchDb.Database("data-protection-keys");

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
        await KeysDb.CreateIfNotExistsAsync();

        var docs = await KeysDb.ListAsync<DataProtectionKeyDoc>();
        return docs
            .Where(d => d.Xml is not null)
            .Select(d => XElement.Parse(d.Xml!))
            .ToList();
    }

    private async Task StoreElementAsync(XElement element, string friendlyName)
    {
        await KeysDb.CreateIfNotExistsAsync();

        var docId = Guid.NewGuid().ToString("N");
        await KeysDb.PutAsync(docId, new DataProtectionKeyDoc
        {
            Id = docId,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            FriendlyName = friendlyName ?? ""
        });
    }

    private class DataProtectionKeyDoc : CouchDocument
    {
        [JsonPropertyName("xml")]
        public string? Xml { get; init; }

        [JsonPropertyName("friendlyName")]
        public string? FriendlyName { get; init; }
    }
}
