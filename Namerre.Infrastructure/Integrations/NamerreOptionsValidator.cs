using Microsoft.Extensions.Options;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Infrastructure.Integrations;

/// <summary>Fails startup for inconsistent <c>Namerre:Integrations</c> settings. Messages never include secrets.</summary>
public sealed class NamerreOptionsValidator : IValidateOptions<NamerreOptions>
{
    public ValidateOptionsResult Validate(string? name, NamerreOptions options)
    {
        var failures = new List<string>();
        var integrations = options.Integrations;
        Check("Jellyfin", "ApiKey", integrations.Jellyfin.Url, integrations.Jellyfin.ApiKey, failures);
        Check("Emby", "ApiKey", integrations.Emby.Url, integrations.Emby.ApiKey, failures);
        Check("Plex", "Token", integrations.Plex.Url, integrations.Plex.Token, failures);
        if (!string.IsNullOrWhiteSpace(integrations.Webhook.Url) && !IsHttpUrl(integrations.Webhook.Url))
            failures.Add("Namerre:Integrations:Webhook:Url must be an absolute http or https URL.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(string target, string secretName, string? url, string? secret, List<string> failures)
    {
        var prefix = "Namerre:Integrations:" + target;
        var hasUrl = !string.IsNullOrWhiteSpace(url);
        var hasSecret = !string.IsNullOrWhiteSpace(secret);
        if (hasUrl && !IsHttpUrl(url)) failures.Add($"{prefix}:Url must be an absolute http or https URL.");
        if (hasUrl && !hasSecret) failures.Add($"{prefix}:{secretName} is required when {prefix}:Url is set.");
        if (hasSecret && !hasUrl) failures.Add($"{prefix}:Url is required when {prefix}:{secretName} is set.");
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
