using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>
/// Starts registering a passkey for the user, after confirming it is them (a recent sign-in or their password).
/// </summary>
public sealed record BeginPasskeyRegistrationCommand(long UserId, bool RecentSignIn, string? Password,
    PasskeyRelyingParty RelyingParty) : IRequest<PasskeyRegistration>;
