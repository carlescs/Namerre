using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Namerre.Infrastructure.Diagnostics;

/// <summary>
/// Namerre's own traces and metrics. The source and meter are static and cost almost nothing until a listener
/// (the optional OpenTelemetry export in the API) subscribes. Metric tags stay low-cardinality — kinds, statuses,
/// outcomes, error codes and configured names — and never carry file paths; record IDs go on spans only.
/// </summary>
public static class NamerreTelemetry
{
    public const string Name = "Namerre";

    /// <summary>Marks an outgoing request whose URL may hold a secret, so HTTP instrumentation must not trace it.</summary>
    public static readonly HttpRequestOptionsKey<bool> Untraced = new("namerre.untraced");

    private static readonly string? Version = typeof(NamerreTelemetry).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public static readonly ActivitySource Source = new(Name, Version);
    public static readonly Meter Meter = new(Name, Version);

    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "namerre.rename.job.duration", "s", "Duration of durable rename jobs (prepare, execute, recover) by final status.");

    public static readonly Counter<long> JobFiles = Meter.CreateCounter<long>(
        "namerre.rename.job.files", "{file}", "Files in finished rename jobs, by job kind and final file status.");

    public static readonly Histogram<double> PreviewDuration = Meter.CreateHistogram<double>(
        "namerre.rename.preview.duration", "s", "Duration of rename previews.");

    public static readonly Counter<long> Rejections = Meter.CreateCounter<long>(
        "namerre.rename.rejections", "{request}", "Rename requests refused because Namerre was busy or blocked.");

    public static readonly Counter<long> HashedBytes = Meter.CreateCounter<long>(
        "namerre.rename.hashed", "By", "Bytes read to fingerprint files for plans, verification, rollback and recovery.");

    public static readonly Counter<long> TmdbLookups = Meter.CreateCounter<long>(
        "namerre.tmdb.lookups", "{lookup}", "TMDB lookups by result: cache_hit, ok or the API error code.");

    public static readonly Counter<long> WatchFiles = Meter.CreateCounter<long>(
        "namerre.watch.files", "{file}", "Watch folder files by outcome (prepared, unchanged, completed, failed, ignored).");

    public static readonly Counter<long> LibraryNotifications = Meter.CreateCounter<long>(
        "namerre.library.notifications", "{notification}", "Library refresh and webhook deliveries by target and outcome.");

    public static readonly Counter<long> SignIns = Meter.CreateCounter<long>(
        "namerre.auth.sign_ins", "{attempt}", "Sign-in attempts by method and outcome.");

    /// <summary>Marks <paramref name="activity"/> as failed with a public error type (never the exception message).</summary>
    public static void Fail(this Activity? activity, Exception exception)
    {
        if (activity is null) return;
        var type = ErrorType(exception);
        activity.SetStatus(ActivityStatusCode.Error, type);
        activity.SetTag("error.type", type);
    }

    /// <summary>A bounded error description: the API error code for rename errors, otherwise the exception type.</summary>
    public static string ErrorType(Exception exception)
        => exception is Application.Renaming.RenameException rename ? rename.Code : exception.GetType().FullName ?? "Exception";
}
