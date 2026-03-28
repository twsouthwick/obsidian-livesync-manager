using obsidian_sync_manager.Web;
using obsidian_sync_manager.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add CouchDB admin client.
builder.Services.AddHttpClient<CouchDbAdminClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["COUCHDB_URL"]
        ?? throw new InvalidOperationException("COUCHDB_URL is not configured."));

    var username = builder.Configuration["COUCHDB_USERNAME"]
        ?? throw new InvalidOperationException("COUCHDB_USERNAME is not configured.");
    var password = builder.Configuration["COUCHDB_PASSWORD"]
        ?? throw new InvalidOperationException("COUCHDB_PASSWORD is not configured.");

    var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
});
builder.Services.AddHostedService<CouchDbInitializer>();

// Configure generic OIDC authentication.
builder.Services.AddOidcAuthentication(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .RequireAuthorization()
    .AddInteractiveServerRenderMode();

app.MapAuthEndpoints();

app.MapDefaultEndpoints();

app.Run();
