using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Unlinks a single sign-on identity from the signed-in user's own account after a recent sign-in, unless it is
/// their last way to sign in.
/// </summary>
public sealed record UnlinkOwnLoginCommand(UserReference Actor, long LoginId, bool RecentSignIn)
    : IRequest<UserRecord>;
