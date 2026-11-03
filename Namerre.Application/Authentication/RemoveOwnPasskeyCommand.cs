using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Removes a passkey from the signed-in user's own account after a recent sign-in, unless it is their last way to
/// sign in.
/// </summary>
public sealed record RemoveOwnPasskeyCommand(UserReference Actor, long PasskeyId, bool RecentSignIn)
    : IRequest<UserRecord>;
