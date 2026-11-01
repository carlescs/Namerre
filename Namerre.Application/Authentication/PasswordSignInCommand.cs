using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Checks a username and password and records the sign-in; null when they do not match an enabled user.</summary>
public sealed record PasswordSignInCommand(string Username, string Password) : IRequest<UserRecord?>;
