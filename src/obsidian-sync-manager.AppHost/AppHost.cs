using Couchbase.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var couchbase = builder.AddCouchbase("couchbase");

builder.AddProject<Projects.obsidian_sync_manager_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(couchbase)
    .WaitFor(couchbase);

builder.Build().Run();
