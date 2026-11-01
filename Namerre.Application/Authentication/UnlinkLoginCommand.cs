using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Unlinks a single sign-on identity from a user, unless it is their last way to sign in.</summary>
public sealed record UnlinkLoginCommand(UserReference Actor, long UserId, long LoginId) : IRequest<UserRecord>;
