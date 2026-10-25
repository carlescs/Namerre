using Microsoft.AspNetCore.SignalR;
using Namerre.Application.Renaming;
using Namerre.Authentication;

namespace Namerre.Notifications;

/// <summary>
/// Broadcasts <see cref="RenameChange"/> signals to every connection signed in with the current
/// administrator credentials; there is a single administrator.
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
            var stamp = services.GetRequiredService<AdminAccount>().Stamp;
            await hub.Clients.Group(RenameHub.StampGroup(stamp)).SendAsync(Method, change);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cannot push rename change {Kind} {Id}", change.Kind, change.Id);
        }
    }
}
