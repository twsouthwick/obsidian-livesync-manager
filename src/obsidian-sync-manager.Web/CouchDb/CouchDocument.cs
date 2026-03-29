using System.Text.Json.Serialization;

namespace Swick.Obsidian.SyncManager.Web.CouchDb;

public abstract class CouchDocument
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("_rev")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rev { get; set; }
}
