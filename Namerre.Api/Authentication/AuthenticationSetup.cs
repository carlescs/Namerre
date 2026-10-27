using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Authentication;

namespace Namerre.Authentication;

public static class AuthenticationSetup
{
    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IAdministratorStore, AdministratorStore>();
        services.AddSingleton<AdminAccount>();
        services.AddSingleton<OidcLogin>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-namerre-session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = context =>
                {
                    var services = context.HttpContext.RequestServices;
                    if (context.Principal is null || !IsValidSession(context.Principal,
                            services.GetRequiredService<AdminAccount>(), services.GetRequiredService<OidcLogin>()))
                        context.RejectPrincipal();
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OidcLogin.Scheme, _ => { });
        // The handler is always registered so its options can bind; the scheme provider hides it unless configured.
        services.AddSingleton<IAuthenticationSchemeProvider, OidcAwareSchemeProvider>();
        services.AddOptions<OpenIdConnectOptions>(OidcLogin.Scheme).Configure<OidcLogin>(ConfigureOidc);
        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "__Host-namerre-antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.Configure<MvcOptions>(options =>
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // One account, one shared limit: changing source IP cannot bypass it.
            options.AddPolicy("login", _ => RateLimitPartition.GetFixedWindowLimiter("administrator",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
        return services;
    }

    internal static bool IsValidSession(ClaimsPrincipal principal, AdminAccount account,
        OidcLogin oidc)
    {
        if (principal.FindFirst(OidcLogin.MethodClaim)?.Value == OidcLogin.Scheme)
            return oidc.IsValidSession(principal);
        return !account.RequiresSetup &&
            principal.FindFirst(AdminAccount.StampClaim)?.Value == account.Stamp;
    }

    private static void ConfigureOidc(OpenIdConnectOptions options, OidcLogin oidc)
    {
        if (!oidc.Enabled)
            return;
        var settings = oidc.Options;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Authority = settings.Authority;
        options.ClientId = settings.ClientId;
        options.ClientSecret = string.IsNullOrEmpty(settings.ClientSecret) ? null : settings.ClientSecret;
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        // The Namerre session keeps its own fixed eight-hour lifetime.
        options.UseTokenLifetime = false;
        options.CallbackPath = OidcLogin.CallbackPath;
        // Sign-out is started by AuthController.Logout; the handler's own sign-out endpoints are not exposed.
        options.SignedOutCallbackPath = PathString.Empty;
        options.RemoteSignOutPath = PathString.Empty;
        foreach (var scope in settings.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
            options.Scope.Add(scope);
        options.Events.OnTokenValidated = context =>
        {
            // Keep only the ID token, as the hint that lets the provider end its session without asking.
            var idToken = context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage.IdToken;
            if (!string.IsNullOrEmpty(idToken))
                context.Properties!.StoreTokens([new AuthenticationToken { Name = OidcLogin.IdTokenName, Value = idToken }]);
            return Task.CompletedTask;
        };
        options.Events.OnTicketReceived = context =>
        {
            var services = context.HttpContext.RequestServices;
            var principal = services.GetRequiredService<OidcLogin>().CreateSessionPrincipal(context.Principal);
            if (principal is null)
            {
                services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OidcLogin)).LogWarning(
                    "Single sign-on rejected subject {Subject}: the required role is missing.",
                    context.Principal?.FindFirst("sub")?.Value);
                // Failing here would still sign in, so end the request explicitly.
                context.Response.Redirect("/?signInError=access-denied");
                context.HandleResponse();
                return Task.CompletedTask;
            }
            context.Principal = principal;
            context.Properties!.IsPersistent = false;
            context.Properties.RedirectUri = "/";
            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OidcLogin))
                .LogWarning(context.Failure, "Single sign-on failed.");
            context.Response.Redirect("/?signInError=failed");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }
}

internal sealed class OidcAwareSchemeProvider : AuthenticationSchemeProvider
{
    public OidcAwareSchemeProvider(IOptions<AuthenticationOptions> options, OidcLogin oidc) : base(options)
    {
        if (!oidc.Enabled)
            RemoveScheme(OidcLogin.Scheme);
    }
}
