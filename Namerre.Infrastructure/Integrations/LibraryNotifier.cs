using System.Diagnostics;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Infrastructure.Integrations;

/// <summary>
/// Refreshes Jellyfin, Emby and Plex libraries and posts a webhook after a run finishes. Runs are queued on a bounded
/// channel and delivered by a background loop, so a slow or failing endpoint never delays or fails a rename job.
/// Tokens are only ever sent as request headers and never logged.
/// </summary>
public sealed class LibraryNotifier(IHttpClientFactory httpClients, IOptions<NamerreOptions> options,
    ILogger<LibraryNotifier> logger) : BackgroundService, ILibraryNotifier
{
    public const string HttpClientName = "library-integrations";
    public const string WebhookEvent = "run.finished";
    private readonly Channel<(RunView Run, ActivityContext Origin)> queue = Channel.CreateBounded<(RunView, ActivityContext)>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    public void RunFinished(RunView run)
    {
        try
        {
            if (!IsConfigured(options.Value.Integrations)) return;
            if (queue.Writer.TryWrite((run, Activity.Current?.Context ?? default))) return;
            Count("Queue", "dropped");
            logger.LogWarning("Library notification queue is full; skipped run {Id}", run.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cannot queue library notification for run {Id}", run.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (run, origin) in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try { await Deliver(run, stoppingToken, origin); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogWarning(exception, "Library notification for run {Id} failed", run.Id); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>Sends the run to every configured target; a failing target is logged and does not stop the others.</summary>
    /// <param name="origin">The span of the job that finished the run, linked from the delivery span.</param>
    public async Task Deliver(RunView run, CancellationToken cancellationToken, ActivityContext origin = default)
    {
        var integrations = options.Value.Integrations;
        using var activity = NamerreTelemetry.Source.StartActivity("library.notify", ActivityKind.Internal,
            parentContext: default, links: origin == default ? null : [new ActivityLink(origin)]);
        activity?.SetTag("namerre.run.id", run.Id);
        activity?.SetTag("namerre.run.status", run.Status);
        if (Configured(integrations.Jellyfin.Url, integrations.Jellyfin.ApiKey))
            await Send("Jellyfin", HttpMethod.Post, Url(integrations.Jellyfin.Url, "/Library/Refresh"),
                "X-Emby-Token", integrations.Jellyfin.ApiKey, null, cancellationToken);
        if (Configured(integrations.Emby.Url, integrations.Emby.ApiKey))
            await Send("Emby", HttpMethod.Post, Url(integrations.Emby.Url, "/emby/Library/Refresh"),
                "X-Emby-Token", integrations.Emby.ApiKey, null, cancellationToken);
        if (Configured(integrations.Plex.Url, integrations.Plex.Token))
            await Send("Plex", HttpMethod.Get, Url(integrations.Plex.Url, "/library/sections/all/refresh"),
                "X-Plex-Token", integrations.Plex.Token, null, cancellationToken);
        if (!string.IsNullOrWhiteSpace(integrations.Webhook.Url))
            await Send("Webhook", HttpMethod.Post, integrations.Webhook.Url!.Trim(), null, null,
                Payload(run, integrations.Webhook.IncludePaths), cancellationToken);
    }

    internal static bool IsConfigured(IntegrationsOptions integrations)
        => Configured(integrations.Jellyfin.Url, integrations.Jellyfin.ApiKey)
            || Configured(integrations.Emby.Url, integrations.Emby.ApiKey)
            || Configured(integrations.Plex.Url, integrations.Plex.Token)
            || !string.IsNullOrWhiteSpace(integrations.Webhook.Url);

    internal static object Payload(RunView run, bool includePaths)
    {
        var files = new Dictionary<string, object>
        {
            ["total"] = run.Files.Count,
            ["completed"] = run.Files.Count(f => f.Status is "Completed" or "Restored"),
            ["failed"] = run.Files.Count(f => f.Status == "Failed")
        };
        var payload = new Dictionary<string, object>
        {
            ["event"] = WebhookEvent,
            ["runId"] = run.Id,
            ["kind"] = run.Kind,
            ["status"] = run.Status,
            ["files"] = files
        };
        if (includePaths)
            files["items"] = run.Files.Select(f => new { sourcePath = f.SourcePath, targetPath = f.TargetPath }).ToArray();
        return payload;
    }

    private async Task Send(string target, HttpMethod method, string url, string? header, string? secret, object? body,
        CancellationToken cancellationToken)
    {
        // A span of its own, without the URL: the HTTP instrumentation skips these requests (see Untraced).
        using var activity = NamerreTelemetry.Source.StartActivity("library.notify " + target, ActivityKind.Client);
        activity?.SetTag("namerre.library.target", target);
        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Options.Set(NamerreTelemetry.Untraced, true);
            if (header is not null) request.Headers.TryAddWithoutValidation(header, secret);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "HTTP " + (int)response.StatusCode);
                activity?.SetTag("error.type", ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                Count(target, "http_error");
                logger.LogWarning("{Target} notification returned HTTP {Status}", target, (int)response.StatusCode);
                return;
            }
            Count(target, "delivered");
            logger.LogInformation("{Target} notification delivered", target);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Never log the request or the exception message: the URL may embed credentials and headers hold the token.
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            activity?.SetTag("error.type", exception.GetType().FullName);
            Count(target, "failed");
            logger.LogWarning("{Target} notification failed: {Reason}", target, exception.GetType().Name);
        }
    }

    private static void Count(string target, string outcome)
        => NamerreTelemetry.LibraryNotifications.Add(1, new KeyValuePair<string, object?>("namerre.library.target", target),
            new KeyValuePair<string, object?>("namerre.library.outcome", outcome));

    private static bool Configured(string? url, string? secret) => !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(secret);
    private static string Url(string? baseUrl, string path) => baseUrl!.Trim().TrimEnd('/') + path;
}
