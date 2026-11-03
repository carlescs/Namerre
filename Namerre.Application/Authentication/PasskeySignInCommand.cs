using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Verifies a passkey sign-in and records it; null when it does not prove an enabled user's registered passkey.
/// </summary>
public sealed record PasskeySignInCommand(string State, string Response) : IRequest<UserRecord?>;
