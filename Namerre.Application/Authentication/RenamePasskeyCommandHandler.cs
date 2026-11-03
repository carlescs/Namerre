using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class RenamePasskeyCommandHandler(UserAccounts accounts)
    : IRequestHandler<RenamePasskeyCommand, UserRecord>
{
    public Task<UserRecord> Handle(RenamePasskeyCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.RenamePasskey(request.Actor, request.UserId, request.PasskeyId, request.Name));
}
