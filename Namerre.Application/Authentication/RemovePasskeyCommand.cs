using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Removes a passkey from a user, unless it is their last way to sign in.</summary>
public sealed record RemovePasskeyCommand(UserReference Actor, long UserId, long PasskeyId) : IRequest<UserRecord>;
