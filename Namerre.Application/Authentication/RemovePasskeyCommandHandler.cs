using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class RemovePasskeyCommandHandler(UserAccounts accounts)
    : IRequestHandler<RemovePasskeyCommand, UserRecord>
{
    public Task<UserRecord> Handle(RemovePasskeyCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.RemovePasskey(request.Actor, request.UserId, request.PasskeyId));
}
