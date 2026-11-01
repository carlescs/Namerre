using Namerre.Application.Renaming;

namespace Namerre.Infrastructure.Configuration;

public sealed class NamerreOptions
{
    public const string SectionName = "Namerre";

    public string? RootPath { get; set; }
    public string? DataPath { get; set; }
    public AuthenticationOptions Authentication { get; set; } = new();
    public TmdbOptions Tmdb { get; set; } = new();
    public ReverseProxyOptions ReverseProxy { get; set; } = new();
    public TelemetryOptions Telemetry { get; set; } = new();
    public LimitsOptions Limits { get; set; } = new();
    public IntegrationsOptions Integrations { get; set; } = new();
    /// <summary>Folders whose new files are renamed automatically with a saved preset.</summary>
    public List<WatchFolderOptions> WatchFolders { get; set; } = [];
}

/// <summary>Workload limits; values outside 1 to the ceilings of <see cref="RenameLimits"/> fail startup.</summary>
public sealed class LimitsOptions
{
    public int MaxFilesPerRun { get; set; } = RenameLimits.DefaultFiles;
    public int MaxStepsPerRun { get; set; } = RenameLimits.DefaultSteps;
    public int MaxQueuedJobs { get; set; } = RenameLimits.DefaultQueuedJobs;
    public int MaxConcurrentPreviews { get; set; } = RenameLimits.DefaultConcurrentPreviews;

    public RenameLimits ToLimits() => new(MaxFilesPerRun, MaxStepsPerRun, MaxQueuedJobs, MaxConcurrentPreviews);
}

public sealed class AuthenticationOptions
{
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public OidcOptions Oidc { get; set; } = new();
}

/// <summary>Optional OpenID Connect sign-in (for example Keycloak). Disabled while <see cref="Authority"/> is empty.</summary>
public sealed class OidcOptions
{
    /// <summary>Issuer URL, for Keycloak <c>https://keycloak.example.com/realms/&lt;realm&gt;</c>.</summary>
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    /// <summary>Client secret of a confidential client. Treat as a secret.</summary>
    public string? ClientSecret { get; set; }
    /// <summary>Label of the sign-in button.</summary>
    public string? DisplayName { get; set; } = "Single sign-on";
    /// <summary>Role (or group) a user must hold to sign in.</summary>
    public string? RequiredRole { get; set; }
    /// <summary>ID token claim holding the roles; dots address nested JSON (Keycloak's default is <c>realm_access.roles</c>).</summary>
    public string? RoleClaim { get; set; } = "realm_access.roles";
    /// <summary>Claim whose value names users created on their first sign-in; the stable <c>sub</c> claim identifies them.</summary>
    public string? UsernameClaim { get; set; } = "preferred_username";
    /// <summary>
    /// Creates a local user (role <c>User</c>) on an identity's first sign-in. When false, identities must be linked
    /// to a user by an administrator or by the user before they can sign in.
    /// </summary>
    public bool AutoProvision { get; set; } = true;
    /// <summary>Scopes requested in addition to <c>openid</c> and <c>profile</c>.</summary>
    public string[] Scopes { get; set; } = [];
    public bool RequireHttpsMetadata { get; set; } = true;
}

public sealed class TmdbOptions
{
    public string? ReadAccessToken { get; set; }
    public string? Language { get; set; } = "en";
}

public sealed class ReverseProxyOptions
{
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
}

public sealed class TelemetryOptions
{
    /// <summary>OTLP collector endpoint. Telemetry is disabled while this is empty.</summary>
    public string? OtlpEndpoint { get; set; }
    /// <summary><c>Grpc</c> (default, usually port 4317) or <c>HttpProtobuf</c> (usually port 4318).</summary>
    public string? Protocol { get; set; } = "Grpc";
    /// <summary>Optional OTLP headers in <c>key1=value1,key2=value2</c> form.</summary>
    public string? Headers { get; set; }
    public string? ServiceName { get; set; } = "namerre";
}

/// <summary>Optional notifications after a run finishes. Each target is disabled while its <c>Url</c> is empty.</summary>
public sealed class IntegrationsOptions
{
    public MediaServerOptions Jellyfin { get; set; } = new();
    public MediaServerOptions Emby { get; set; } = new();
    public PlexOptions Plex { get; set; } = new();
    public WebhookOptions Webhook { get; set; } = new();
}

public sealed class MediaServerOptions
{
    public string? Url { get; set; }
    /// <summary>API key sent as <c>X-Emby-Token</c>. Treat as a secret.</summary>
    public string? ApiKey { get; set; }
}

public sealed class PlexOptions
{
    public string? Url { get; set; }
    /// <summary>Plex token sent as <c>X-Plex-Token</c>. Treat as a secret.</summary>
    public string? Token { get; set; }
}

public sealed class WebhookOptions
{
    public string? Url { get; set; }
    /// <summary>Include each file's source and target path in the payload. Paths may be sensitive.</summary>
    public bool IncludePaths { get; set; }
}
