using System.Text.Json;
using System.Text.Json.Serialization;

namespace Swick.Obsidian.SyncManager.Web.CouchDb;

public class CouchDbUsers(CouchDbClient couchDb)
{
    private CouchDatabase UsersDb => couchDb.Database("_users");

    public async Task CreateIfNotExistsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var userId = $"org.couchdb.user:{username}";
        var existing = await UsersDb.GetAsync<UserDoc>(userId, cancellationToken);

        await UsersDb.PutAsync(userId, new UserDoc
        {
            Id = userId,
            Rev = existing?.Rev,
            Name = username,
            Password = password,
            Type = "user",
            Roles = []
        }, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        var userId = $"org.couchdb.user:{username}";
        return await UsersDb.GetAsync<UserDoc>(userId, cancellationToken) is not null;
    }

    private class UserDoc : CouchDocument
    {
        [JsonPropertyName("name")]      public string Name { get; init; } = "";
        [JsonPropertyName("password")]  public string Password { get; init; } = "";
        [JsonPropertyName("type")]      public string Type { get; init; } = "user";
        [JsonPropertyName("roles")]     public string[] Roles { get; init; } = [];
    }
}
