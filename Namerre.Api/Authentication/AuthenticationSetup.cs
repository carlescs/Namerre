using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Authentication;

public static class AuthenticationSetup
{
    // Login, setup and password changes share these limits; each attempt costs a full password hash.
    public const string LoginPolicy = "login";
    internal const int LoginLimitPerClient = 5;
    internal const int LoginLimitShared = 60;

    /// <summary>Authorization policy for administrator-only endpoints.</summary>
    public const string AdminPolicy = "RequireAdmin";

    public static IServiceCollection AddUserAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<UserAccounts>();
        services.AddSingleton<OidcLogin>();
        services.AddHttpContextAccessor();
        services.AddSingleton<ICurrentUser, HttpCurrentUser>();
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
                            services.GetRequiredService<UserAccounts>(), services.GetRequiredService<OidcLogin>()))
                        context.RejectPrincipal();
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OidcLogin.Scheme, _ => { });
        // The handler is always registered so its options can bind; the scheme provider hides it unless configured.
        services.AddSingleton<IAuthenticationSchemeProvider, OidcAwareSchemeProvider>();
        services.AddOptions<OpenIdConnectOptions>(OidcLogin.Scheme).Configure<OidcLogin>(ConfigureOidc);
        services.AddAuthorization(options =>
            options.AddPolicy(AdminPolicy, policy => policy.RequireRole(nameof(UserRole.Admin))));
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
            // Each client gets its own small budget, so one source cannot lock the administrator out...
            options.AddPolicy(LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientPartition(context.Connection.RemoteIpAddress), _ => LoginWindow(LoginLimitPerClient)));
            // ...and a shared ceiling still bounds guessing from many addresses at once.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == LoginPolicy
                    ? RateLimitPartition.GetFixedWindowLimiter("administrator", _ => LoginWindow(LoginLimitShared))
                    : RateLimitPartition.GetNoLimiter(""));
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions LoginWindow(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };

    /// <summary>
    /// Groups clients by address, after forwarded headers are applied. IPv6 clients are grouped by their /64
    /// prefix, since a single host usually controls a whole /64 and could otherwise rotate addresses.
    /// </summary>
    internal static string ClientPartition(IPAddress? address)
    {
        if (address is null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().ToString();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>
    /// A session stays valid while its user exists, is enabled and keeps the same credential stamp (and, for single
    /// sign-on sessions, while single sign-on settings are unchanged). The stamp changes with the user's role,
    /// password and links, so the role claim never outlives a change.
    /// </summary>
    internal static bool IsValidSession(ClaimsPrincipal principal, UserAccounts accounts, OidcLogin oidc)
    {
        if (UserSession.UserId(principal) is not { } id || accounts.Find(id) is not { Disabled: false } user ||
            principal.FindFirst(UserSession.StampClaim)?.Value != user.Stamp ||
            principal.FindFirst(ClaimTypes.Role)?.Value != user.Role.ToString())
            return false;
        return !UserSession.IsSingleSignOn(principal) || oidc.IsValidSession(principal);
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
        options.Events.OnTicketReceived = OnTicketReceived;
        options.Events.OnRemoteFailure = context =>
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OidcLogin))
                .LogWarning(context.Failure, "Single sign-on failed.");
            NamerreTelemetry.SignIns.Add(1, new("namerre.auth.method", "oidc"), new("namerre.auth.outcome", "failure"));
            context.Response.Redirect("/?signInError=failed");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// When the provider authenticated the user (the ID token's <c>auth_time</c>), never later than now; now when
    /// the provider does not say. Challenges that need a fresh sign-in request <c>max_age=0</c>, which makes
    /// providers include it.
    /// </summary>
    internal static DateTimeOffset AuthenticatedAt(System.Security.Claims.ClaimsPrincipal? principal, DateTimeOffset now)
        => long.TryParse(principal?.FindFirst("auth_time")?.Value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds) &&
            seconds <= now.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : now;

    /// <summary>
    /// Turns a validated single sign-on into a session of the linked local user, or links the identity when the
    /// challenge was started by <c>AuthController.LinkSingleSignOn</c>. Failing here would still sign in, so every
    /// refusal ends the request with a redirect.
    /// </summary>
    private static Task OnTicketReceived(TicketReceivedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var oidc = services.GetRequiredService<OidcLogin>();
        var accounts = services.GetRequiredService<UserAccounts>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OidcLogin));
        var now = services.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var properties = context.Properties!;
        var linking = properties.Items.TryGetValue(OidcLogin.LinkUserItem, out var linkUser);

        Task Refuse(string location, string outcome)
        {
            NamerreTelemetry.SignIns.Add(1, new("namerre.auth.method", "oidc"), new("namerre.auth.outcome", outcome));
            context.Response.Redirect(location);
            context.HandleResponse();
            return Task.CompletedTask;
        }

        var identity = oidc.Read(context.Principal);
        if (identity is null)
        {
            logger.LogWarning("Single sign-on rejected subject {Subject}: the required role is missing.",
                LogText.Clean(context.Principal?.FindFirst("sub")?.Value));
            return Refuse(linking ? "/account?linkError=access-denied" : "/?signInError=access-denied", "denied");
        }

        UserRecord user;
        long? loginId;
        if (linking)
        {
            try
            {
                var id = long.Parse(linkUser!, System.Globalization.CultureInfo.InvariantCulture);
                var current = accounts.Find(id);
                if (current is null || current.Disabled)
                    return Refuse("/account?linkError=failed", "denied");
                user = accounts.Link(new UserReference(current.Id, current.Username), id, identity,
                    (properties.Items.TryGetValue(OidcLogin.LinkStampItem, out var stamp) ? stamp : null) ?? "");
                loginId = user.Logins.Single(l => l.Issuer == identity.Issuer && l.Subject == identity.Subject).Id;
            }
            catch (AccountException exception)
            {
                logger.LogWarning("Linking single sign-on was refused: {Reason}", exception.Code);
                return Refuse(exception.Code == "LoginTaken" || exception.Code == "AlreadyLinked"
                    ? "/account?linkError=taken"
                    : "/account?linkError=failed", "denied");
            }
            catch (UserStorageException exception)
            {
                logger.LogError(exception, "Unable to save a single sign-on link.");
                return Refuse("/account?linkError=failed", "failure");
            }
            context.ReturnUri = "/account?linked=1";
        }
        else
        {
            var (resolved, resolvedLogin, failure) = accounts.SignInExternal(identity, oidc.Options.AutoProvision);
            if (resolved is null)
            {
                logger.LogWarning("Single sign-on rejected subject {Subject} from issuer {Issuer}: {Reason}.",
                    LogText.Clean(identity.Subject), LogText.Clean(identity.Issuer), failure);
                return Refuse(failure switch
                {
                    ExternalSignInFailure.NotLinked => "/?signInError=not-linked",
                    ExternalSignInFailure.SetupRequired => "/?signInError=setup-required",
                    ExternalSignInFailure.Unavailable => "/?signInError=unavailable",
                    _ => "/?signInError=access-denied"
                }, failure == ExternalSignInFailure.Unavailable ? "failure" : "denied");
            }
            user = resolved;
            loginId = resolvedLogin;
            // Only fixed return addresses, which avoids open redirects.
            context.ReturnUri = context.ReturnUri == "/account" ? "/account" : "/";
        }

        accounts.RecordSignIn(user, loginId);
        NamerreTelemetry.SignIns.Add(1, new("namerre.auth.method", "oidc"), new("namerre.auth.outcome", "success"));
        logger.LogInformation("User {UserId} ({Username}) signed in with single sign-on.", user.Id, LogText.Clean(user.Username));
        // The provider's authentication time, not the callback's, decides whether the sign-in counts as recent:
        // a provider session reused without signing in again must not unlock sensitive changes.
        context.Principal = UserSession.Create(user, UserSession.SingleSignOnMethod, oidc.Stamp,
            AuthenticatedAt(context.Principal, now));
        properties.IsPersistent = false;
        properties.Items.Remove(OidcLogin.LinkUserItem);
        properties.Items.Remove(OidcLogin.LinkStampItem);
        return Task.CompletedTask;
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
