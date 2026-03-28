using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace obsidian_sync_manager.Web;

public static class AuthenticationExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddOidcAuthentication(IConfiguration configuration)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                configuration.GetSection("OIDC").Bind(options);
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.MapInboundClaims = false;
                options.SaveTokens = true;
                options.TokenValidationParameters.RoleClaimType = "groups";
            });

            services.AddCascadingAuthenticationState();

            services.AddOptions<OidcGroupOptions>()
                .BindConfiguration("OIDC:Groups")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddAuthorization();
            services.AddOptions<AuthorizationOptions>()
                .Configure<IOptions<OidcGroupOptions>>((options, groupOptions) =>
                {
                    var groups = groupOptions.Value;
                    options.AddPolicy("Admin", policy => policy.RequireRole(groups.Admins));
                    options.AddPolicy("User", policy => policy.RequireRole(groups.Users, groups.Admins));
                });

            return services;
        }
    }

    extension(WebApplication app)
    {
        public WebApplication MapAuthEndpoints()
        {
            app.MapGet("/login", (string? returnUrl) =>
                TypedResults.Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/" }));

            app.MapPost("/logout", async (HttpContext context) =>
            {
                var idToken = await context.GetTokenAsync("id_token");
                var properties = new AuthenticationProperties { RedirectUri = "/" };
                if (idToken is not null)
                    properties.SetParameter("id_token_hint", idToken);

                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
            }).RequireAuthorization();

            return app;
        }
    }
}
