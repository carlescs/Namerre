using Microsoft.AspNetCore.SignalR;
using Namerre.Application.Renaming;
using Namerre.Authentication;

namespace Namerre.Notifications;

/// <summary>
/// Broadcasts <see cref="RenameChange"/> signals to every connection signed in with the current
/// administrator credentials or current single sign-on settings; there is a single administrator.
/// Sends are fire-and-forget so the rename worker never waits on a slow client.
/// </summary>
public sealed class HubRenameNotifier(IHubContext<RenameHub> hub, IServiceProvider services,
    ILogger<HubRenameNotifier> logger) : IRenameNotifier
{
    public const string Method = "renameChanged";

    public void Changed(RenameChange change) => _ = Send(change);

    private async Task Send(RenameChange change)
    {
        try
        {
            // Resolved on send so the rename worker does not load the administrator at startup.
            List<string> groups = [RenameHub.StampGroup(services.GetRequiredService<AdminAccount>().Stamp)];
            // Single sign-on sessions carry their own stamp (see OidcLogin).
            var oidc = services.GetRequiredService<OidcLogin>();
            if (oidc.Enabled)
                groups.Add(RenameHub.StampGroup(oidc.Stamp));
            await hub.Clients.Groups(groups).SendAsync(Method, change);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cannot push rename change {Kind} {Id}", change.Kind, change.Id);
        }
    }
}
