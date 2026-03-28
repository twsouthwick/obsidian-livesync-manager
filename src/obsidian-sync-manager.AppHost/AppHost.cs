var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.obsidian_sync_manager_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.obsidian_sync_manager_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
