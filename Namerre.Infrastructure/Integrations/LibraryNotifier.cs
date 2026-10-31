using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Runs;
using Namerre.Infrastructure.Configuration;

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
    private readonly Channel<RunView> queue = Channel.CreateBounded<RunView>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    public void RunFinished(RunView run)
    {
        try
        {
            if (!IsConfigured(options.Value.Integrations)) return;
            if (!queue.Writer.TryWrite(run))
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
            await foreach (var run in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try { await Deliver(run, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogWarning(exception, "Library notification for run {Id} failed", run.Id); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>Sends the run to every configured target; a failing target is logged and does not stop the others.</summary>
    public async Task Deliver(RunView run, CancellationToken cancellationToken)
    {
        var integrations = options.Value.Integrations;
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
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (header is not null) request.Headers.TryAddWithoutValidation(header, secret);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("{Target} notification returned HTTP {Status}", target, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Never log the request or the exception message: the URL may embed credentials and headers hold the token.
            logger.LogWarning("{Target} notification failed: {Reason}", target, exception.GetType().Name);
        }
    }

    private static bool Configured(string? url, string? secret) => !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(secret);
    private static string Url(string? baseUrl, string path) => baseUrl!.Trim().TrimEnd('/') + path;
}
