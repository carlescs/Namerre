using System.Diagnostics;
using System.Reflection;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Diagnostics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Namerre.Telemetry;

public static class TelemetrySetup
{
    public const string SectionName = $"{NamerreOptions.SectionName}:Telemetry";

    /// <summary>
    /// Exports traces, metrics and logs over OTLP when <c>Namerre:Telemetry:OtlpEndpoint</c> is set;
    /// otherwise registers nothing.
    /// </summary>
    public static WebApplicationBuilder AddOptionalTelemetry(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(SectionName).Get<TelemetryOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(settings.OtlpEndpoint))
            return builder;

        var endpoint = ParseEndpoint(settings.OtlpEndpoint);
        var protocol = ParseProtocol(settings.Protocol);
        var serviceName = string.IsNullOrWhiteSpace(settings.ServiceName) ? "namerre" : settings.ServiceName;
        var serviceVersion = typeof(TelemetrySetup).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        void Configure(OtlpExporterOptions options, string signalPath)
        {
            options.Protocol = protocol;
            // Explicit endpoints are used verbatim, so HTTP needs the per-signal path appended.
            options.Endpoint = protocol == OtlpExportProtocol.HttpProtobuf
                ? new Uri(endpoint, endpoint.AbsolutePath.TrimEnd('/') + signalPath)
                : endpoint;
            if (!string.IsNullOrWhiteSpace(settings.Headers))
                options.Headers = settings.Headers;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                // Rename jobs and their phases, previews, watch folder scans and library notifications.
                .AddSource(NamerreTelemetry.Name)
                .AddSource("Microsoft.AspNetCore.SignalR.Server")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation(options => options.FilterHttpRequestMessage = IsTraced)
                // One span per EF Core database command (the SQLite rename store), inside a request or job span.
                .AddEntityFrameworkCoreInstrumentation(options => options.Filter = (_, _) => HasParent())
                .AddOtlpExporter(options => Configure(options, "/v1/traces")))
            .WithMetrics(metrics => metrics
                .AddMeter(NamerreTelemetry.Name)
                .AddInstrumentation(provider => provider.GetRequiredService<RenameStateMetrics>())
                .AddAspNetCoreInstrumentation()
                .AddMeter("Microsoft.AspNetCore.Authentication", "Microsoft.AspNetCore.Authorization")
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Retries, rejections and circuit state from the TMDB resilience pipeline.
                .AddMeter("Polly")
                .AddOtlpExporter(options => Configure(options, "/v1/metrics")))
            .WithLogging(logging => logging
                .AddOtlpExporter(options => Configure(options, "/v1/logs")),
                options => options.IncludeFormattedMessage = true);

        return builder;
    }

    /// <summary>False for requests whose URL may embed a secret (library webhooks); they get their own span instead.</summary>
    internal static bool IsTraced(HttpRequestMessage request)
        => !request.Options.TryGetValue(NamerreTelemetry.Untraced, out var untraced) || !untraced;

    /// <summary>
    /// True when the database command runs inside another span. The job worker and watch folders poll the store
    /// every few hundred milliseconds; without this, each idle poll would export a trace of its own.
    /// </summary>
    internal static bool HasParent()
        => Activity.Current is { } current
           && (current.Source.Name != EntityFrameworkSource || current.Parent is not null || current.HasRemoteParent);

    private const string EntityFrameworkSource = "OpenTelemetry.Instrumentation.EntityFrameworkCore";

    private static Uri ParseEndpoint(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var endpoint) &&
           endpoint.Scheme is "http" or "https"
            ? endpoint
            : throw new InvalidOperationException(
                $"{SectionName}:OtlpEndpoint must be an absolute http or https URL.");

    private static OtlpExportProtocol ParseProtocol(string? value)
        => string.IsNullOrWhiteSpace(value) ? OtlpExportProtocol.Grpc
            : Enum.TryParse<OtlpExportProtocol>(value, ignoreCase: true, out var protocol) &&
              Enum.IsDefined(protocol)
                ? protocol
                : throw new InvalidOperationException(
                    $"{SectionName}:Protocol must be Grpc or HttpProtobuf.");
}
