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

    // Notifications only go to the groups of current sessions (see UserSession.Group), so connections opened
    // before a password or role change stop receiving them even though the socket outlives the cookie check.
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId,
            UserSession.Group(Context.User ?? new()), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
