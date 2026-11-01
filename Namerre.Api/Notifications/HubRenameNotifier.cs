using Microsoft.AspNetCore.SignalR;
using Namerre.Application.Renaming;
using Namerre.Authentication;

namespace Namerre.Notifications;

/// <summary>
/// Broadcasts <see cref="RenameChange"/> signals to every connection whose session is still current: an enabled user
/// with an unchanged credential stamp (and unchanged single sign-on settings). Run history is shared by all users.
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
            // Resolved on send so the rename worker does not load users at startup.
            var oidc = services.GetRequiredService<OidcLogin>();
            List<string> groups = [];
            foreach (var user in services.GetRequiredService<UserAccounts>().List().Where(u => !u.Disabled))
            {
                groups.Add(UserSession.Group(user.Stamp, null));
                // Single sign-on sessions also carry the settings stamp (see OidcLogin).
                if (oidc.Enabled)
                    groups.Add(UserSession.Group(user.Stamp, oidc.Stamp));
            }
            await hub.Clients.Groups(groups).SendAsync(Method, change);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cannot push rename change {Kind} {Id}", change.Kind, change.Id);
        }
    }
}
