using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class BeginPasskeyRegistrationCommandHandler(PasskeyAccounts passkeys)
    : IRequestHandler<BeginPasskeyRegistrationCommand, PasskeyRegistration>
{
    public Task<PasskeyRegistration> Handle(BeginPasskeyRegistrationCommand request,
        CancellationToken cancellationToken)
        => Task.FromResult(passkeys.BeginRegistration(request.UserId, request.RecentSignIn, request.Password,
            request.RelyingParty));
}
