namespace obsidian_sync_manager.Web;

public class CouchDbInitializer(
    CouchDbAdminClient couchDbAdminClient,
    ILogger<CouchDbInitializer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 10;
        const int delayMs = 2000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation("Initializing CouchDB (attempt {Attempt}/{MaxRetries})...", attempt, maxRetries);
                await couchDbAdminClient.InitializeAsync(cancellationToken);
                await couchDbAdminClient.CreateRegistryDatabaseAsync(cancellationToken);
                logger.LogInformation("CouchDB initialized successfully.");
                return;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                logger.LogWarning(ex, "CouchDB not ready (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms...", attempt, maxRetries, delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }
    }
}
