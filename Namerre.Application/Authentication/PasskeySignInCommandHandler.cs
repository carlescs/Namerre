using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class PasskeySignInCommandHandler(PasskeyAccounts passkeys)
    : IRequestHandler<PasskeySignInCommand, UserRecord?>
{
    public Task<UserRecord?> Handle(PasskeySignInCommand request, CancellationToken cancellationToken)
        => passkeys.SignIn(request.State, request.Response, cancellationToken);
}
