using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class CompletePasskeyRegistrationCommandHandler(PasskeyAccounts passkeys)
    : IRequestHandler<CompletePasskeyRegistrationCommand, UserRecord>
{
    public Task<UserRecord> Handle(CompletePasskeyRegistrationCommand request, CancellationToken cancellationToken)
        => passkeys.CompleteRegistration(request.Actor, request.ExpectedStamp, request.State, request.Response,
            request.Name, cancellationToken);
}
