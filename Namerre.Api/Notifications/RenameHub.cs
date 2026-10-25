using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Namerre.Authentication;

namespace Namerre.Notifications;

/// <summary>
/// Server-to-client channel for plan and run changes. Clients only listen; it exposes no callable methods.
/// </summary>
[Authorize]
public sealed class RenameHub : Hub
{
    public const string Path = "/api/hubs/rename";

    // Notifications only go to the current credentials' group, so connections opened before a
    // password change stop receiving them even though the socket outlives the cookie check.
    public static string StampGroup(string stamp) => "stamp:" + stamp;

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId,
            StampGroup(Context.User?.FindFirst(AdminAccount.StampClaim)?.Value ?? ""), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
