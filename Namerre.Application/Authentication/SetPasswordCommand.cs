using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Sets a user's password. Users changing their own confirm the current one, or need a recent sign-in when they
/// have none yet; an administrator resetting someone else's needs neither.
/// </summary>
public sealed record SetPasswordCommand(UserReference Actor, long UserId, string? CurrentPassword, string NewPassword,
    bool RecentSignIn = false) : IRequest<UserRecord>;
