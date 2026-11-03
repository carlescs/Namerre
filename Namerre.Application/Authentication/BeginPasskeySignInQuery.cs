using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

/// <summary>Starts a passkey sign-in with any discoverable passkey of the relying party.</summary>
public sealed record BeginPasskeySignInQuery(PasskeyRelyingParty RelyingParty) : IRequest<PasskeyChallenge>;
