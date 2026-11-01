using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Confirms a sensitive change to the user's own account: it needs a recent sign-in, or the user's password
/// when they have one. Returns the user; fails when the user cannot be changed here.
/// </summary>
public sealed record ConfirmOwnChangeCommand(long UserId, bool RecentSignIn, string? Password) : IRequest<UserRecord>;
