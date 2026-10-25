using Microsoft.AspNetCore.SignalR;
using Namerre.Application.Renaming;

namespace Namerre.Notifications;

/// <summary>
/// Broadcasts <see cref="RenameChange"/> signals to every connection; there is a single administrator.
/// Sends are fire-and-forget so the rename worker never waits on a slow client.
/// </summary>
public sealed class HubRenameNotifier(IHubContext<RenameHub> hub, ILogger<HubRenameNotifier> logger) : IRenameNotifier
{
    public const string Method = "renameChanged";

    public void Changed(RenameChange change) => _ = Send(change);

    private async Task Send(RenameChange change)
    {
        try { await hub.Clients.All.SendAsync(Method, change); }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cannot push rename change {Kind} {Id}", change.Kind, change.Id);
        }
    }
}
