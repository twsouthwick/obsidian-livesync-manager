var builder = DistributedApplication.CreateBuilder(args);

var couchdbUsername = builder.AddParameter("couchdb-username");
var couchdbPassword = builder.AddParameter("couchdb-password", secret: true);
var couchdbUserSecret = builder.AddParameter("couchdb-user-secret", secret: true);

var couchdb = builder.AddContainer("couchdb", "couchdb", "latest")
    .WithEnvironment("COUCHDB_USER", couchdbUsername)
    .WithEnvironment("COUCHDB_PASSWORD", couchdbPassword)
    .WithHttpEndpoint(targetPort: 5984)
    .WithVolume("couchdb-data", "/opt/couchdb/data");

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("./Realms");

builder.AddProject<Projects.obsidian_sync_manager_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WaitFor(couchdb)
    .WaitFor(keycloak)
    .WithEnvironment("COUCHDB__URL", couchdb.GetEndpoint("http"))
    .WithEnvironment("COUCHDB__USERNAME", couchdbUsername)
    .WithEnvironment("COUCHDB__PASSWORD", couchdbPassword)
    .WithEnvironment("COUCHDB__USERSECRET", couchdbUserSecret)
    .WithEnvironment("OIDC__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("https")}/realms/obsidian-sync"))
    .WithEnvironment("OIDC__ClientId", "obsidian-web");

builder.Build().Run();
