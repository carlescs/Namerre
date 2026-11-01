using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Removes the signed-in user's own password after a recent sign-in, when they have another way to sign in.</summary>
public sealed record RemoveOwnPasswordCommand(UserReference Actor, bool RecentSignIn) : IRequest<UserRecord>;
