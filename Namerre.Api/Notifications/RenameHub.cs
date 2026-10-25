using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Namerre.Notifications;

/// <summary>
/// Server-to-client channel for plan and run changes. Clients only listen; it exposes no callable methods.
/// </summary>
[Authorize]
public sealed class RenameHub : Hub
{
    public const string Path = "/api/hubs/rename";
}
