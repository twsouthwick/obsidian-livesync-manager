using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace obsidian_sync_manager.Web;

public static class CouchDbExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public void AddCouchDb()
        {
            builder.Services.AddOptions<CouchDbOptions>()
                .Bind(builder.Configuration.GetSection("CouchDb"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            void ConfigureHttpClient(IServiceProvider sp, HttpClient client)
            {
                var options = sp.GetRequiredService<IOptions<CouchDbOptions>>().Value;
                client.BaseAddress = options.Url;
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            builder.Services.AddHttpClient<CouchDbClient>(ConfigureHttpClient);
            builder.Services.AddHttpClient<CouchDbXmlRepository>(ConfigureHttpClient);

            builder.Services.AddSingleton<IXmlRepository>(sp => sp.GetRequiredService<CouchDbXmlRepository>());

            builder.Services.AddDataProtection()
                .SetApplicationName("obsidian-sync-manager");

            builder.Services.AddSingleton<HmacSecretProvider>();
            builder.Services.AddSingleton<IUserSecretProvider>(sp => sp.GetRequiredService<HmacSecretProvider>());
            builder.Services.AddHostedService<CouchDbInitializer>();
            builder.Services.AddScoped<WorkspaceService>();
        }
    }

    private class CouchDbInitializer(
        CouchDbClient couchDbAdminClient,
        HmacSecretProvider hmacSecretProvider,
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
                    await hmacSecretProvider.InitializeAsync(cancellationToken);
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
}

public class CouchDbOptions
{
    [Required]
    public Uri Url { get; set; } = null!;

    [Required]
    public string Username { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;
}
