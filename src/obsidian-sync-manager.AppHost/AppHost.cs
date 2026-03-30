var builder = DistributedApplication.CreateBuilder(args);

var couchdbUsername = builder.AddParameter("couchdb-username");
var couchdbPassword = builder.AddParameter("couchdb-password", secret: true);
var oidcClientSecret = builder.AddParameter("oidc-client-secret", secret: true);

var couchdb = builder.AddContainer("couchdb", "couchdb", "latest")
    .WithEnvironment("COUCHDB_USER", couchdbUsername)
    .WithEnvironment("COUCHDB_PASSWORD", couchdbPassword)
    .WithHttpEndpoint(targetPort: 5984)
    .WithVolume("couchdb-data", "/opt/couchdb/data")
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Fauxton";
        url.Url = "/_utils";
    });

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("./Realms")
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "http";
        url.Url = "/";
    })
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "https";
        url.Url = "/";
    });

builder.AddProject<Projects.obsidian_sync_manager_Web>("obsidian-manager")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WaitFor(couchdb)
    .WaitFor(keycloak)
    .WithEnvironment("COUCHDB__URL", couchdb.GetEndpoint("http"))
    .WithEnvironment("COUCHDB__USERNAME", couchdbUsername)
    .WithEnvironment("COUCHDB__PASSWORD", couchdbPassword)
    .WithEnvironment("OIDC__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("https")}/realms/obsidian-sync"))
    .WithEnvironment("OIDC__ClientId", "obsidian-web")
    .WithEnvironment("OIDC__ClientSecret", oidcClientSecret)
    .WithDataProtectionDevCertificate()
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "http";
        url.Url = "/";
    })
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "https";
        url.Url = "/";
    });

builder.Build().Run();
