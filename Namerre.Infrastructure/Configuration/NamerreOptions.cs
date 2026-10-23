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
}

public sealed class AuthenticationOptions
{
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
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
