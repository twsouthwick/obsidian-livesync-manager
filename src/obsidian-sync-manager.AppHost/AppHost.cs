var builder = DistributedApplication.CreateBuilder(args);

var couchdbUsername = builder.AddParameter("couchdb-username");
var couchdbPassword = builder.AddParameter("couchdb-password", secret: true);

var couchdb = builder.AddContainer("couchdb", "couchdb", "latest")
    .WithEnvironment("COUCHDB_USER", couchdbUsername)
    .WithEnvironment("COUCHDB_PASSWORD", couchdbPassword)
    .WithHttpEndpoint(targetPort: 5984)
    .WithVolume("couchdb-data", "/opt/couchdb/data")
    .WithHttpHealthCheck("/_up");

builder.AddProject<Projects.obsidian_sync_manager_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WaitFor(couchdb)
    .WithEnvironment("COUCHDB_URL", couchdb.GetEndpoint("http"))
    .WithEnvironment("COUCHDB_USERNAME", couchdbUsername)
    .WithEnvironment("COUCHDB_PASSWORD", couchdbPassword);

builder.Build().Run();
