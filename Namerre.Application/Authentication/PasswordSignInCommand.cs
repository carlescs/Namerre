using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Checks a username and password and records the sign-in. Attempts on an account are slowed down after repeated
/// failures (<see cref="SignInThrottle"/>), unless they come from <paramref name="Device"/>, a browser that user
/// signed in from before.
/// </summary>
public sealed record PasswordSignInCommand(string Username, string Password, TrustedDevice? Device = null)
    : IRequest<PasswordSignInResult>;
