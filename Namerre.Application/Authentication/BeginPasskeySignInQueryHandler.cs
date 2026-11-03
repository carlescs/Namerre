using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class BeginPasskeySignInQueryHandler(PasskeyAccounts passkeys)
    : IRequestHandler<BeginPasskeySignInQuery, PasskeyChallenge>
{
    public Task<PasskeyChallenge> Handle(BeginPasskeySignInQuery request, CancellationToken cancellationToken)
        => Task.FromResult(passkeys.BeginSignIn(request.RelyingParty));
}
