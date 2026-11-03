using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Ends every session the user has signed in so far. Returns the session epoch that the caller re-issues its own
/// session with, which keeps that one.
/// </summary>
public sealed record EndOtherSessionsCommand(UserReference User) : IRequest<long>;
