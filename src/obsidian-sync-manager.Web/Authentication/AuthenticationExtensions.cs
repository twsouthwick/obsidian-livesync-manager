using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Swick.Obsidian.SyncManager.Web;

public static class AuthenticationExtensions
{
    public const string AdminPolicy = "Admin";
    public const string UserPolicy = "User";

    extension(IHostApplicationBuilder builder)
    {
        public void AddApplicationAuthentication()
        {
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                builder.Configuration.GetSection("OIDC").Bind(options);
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.MapInboundClaims = false;
                options.SaveTokens = true;
                options.TokenValidationParameters.RoleClaimType =
                    builder.Configuration["OIDC:RoleClaimType"] ?? "groups";
                options.Scope.Add(options.TokenValidationParameters.RoleClaimType);
            });

            builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
                .PostConfigure<ILoggerFactory>((options, loggerFactory) =>
                {
                    var logger = loggerFactory.CreateLogger("Swick.Obsidian.SyncManager.Web.Authentication");

                    options.Events.OnTokenValidated = ctx =>
                    {
                        var username = ctx.Principal?.FindFirst("preferred_username")?.Value ?? "(unknown)";
                        var roleClaim = ctx.Options.TokenValidationParameters.RoleClaimType;
                        var groups = ctx.Principal?.FindAll(roleClaim).Select(c => c.Value) ?? [];
                        logger.LogInformation("User {Username} authenticated. Groups: [{Groups}]",
                            username, string.Join(", ", groups));

                        var allClaims = ctx.Principal?.Claims
                            .Where(c => c.Type == roleClaim)
                            .Select(c => c.Value) ?? [];
                        logger.LogDebug("User {Username} {RoleClaim} claims: [{Claims}]",
                            username, roleClaim, string.Join(", ", allClaims));

                        return Task.CompletedTask;
                    };

                    options.Events.OnAuthenticationFailed = ctx =>
                    {
                        logger.LogError(ctx.Exception, "OIDC authentication failed");
                        return Task.CompletedTask;
                    };

                    options.Events.OnRemoteFailure = ctx =>
                    {
                        logger.LogError(ctx.Failure, "OIDC remote failure: {Error}", ctx.Failure?.Message);
                        ctx.HandleResponse();
                        ctx.Response.Redirect("/");
                        return Task.CompletedTask;
                    };
                });

            builder.Services.AddCascadingAuthenticationState();

            builder.Services.AddOptions<OidcGroupOptions>()
                .BindConfiguration("OIDC:Groups")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            builder.Services.AddAuthorization();
            builder.Services.AddOptions<AuthorizationOptions>()
                .Configure<IOptions<OidcGroupOptions>>((options, groupOptions) =>
                {
                    var groups = groupOptions.Value;
                    options.AddPolicy(AdminPolicy, policy => policy.RequireRole(groups.Admins));
                    options.AddPolicy(UserPolicy, policy => policy.RequireRole(groups.Users, groups.Admins));
                });
        }
    }

    extension(IEndpointRouteBuilder app)
    {
        public void MapAuthEndpoints()
        {
            app.MapGet("/login", (string? returnUrl) =>
                TypedResults.Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/" }))
                .AllowAnonymous();

            app.MapPost("/logout", async (HttpContext context) =>
            {
                var idToken = await context.GetTokenAsync("id_token");
                var properties = new AuthenticationProperties { RedirectUri = "/Account/SignedOut" };
                if (idToken is not null)
                    properties.SetParameter("id_token_hint", idToken);

                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                try
                {
                    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
                }
                catch (InvalidOperationException)
                {
                    // OIDC provider may not expose an end_session_endpoint.
                    // Cookie is already cleared; land on anonymous page to avoid SSO re-auth loop.
                    context.Response.Redirect("/Account/SignedOut");
                }
            }).RequireAuthorization();
        }
    }
}
