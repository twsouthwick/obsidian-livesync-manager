using Swick.Obsidian.SyncManager.Web;
using Swick.Obsidian.SyncManager.Web.Components;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddCouchDb();
builder.AddApplicationAuthentication();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

builder.Services.AddOutputCache();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.All;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

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
app.MapWorkspaceApi();
app.MapDefaultEndpoints();

app.Run();
