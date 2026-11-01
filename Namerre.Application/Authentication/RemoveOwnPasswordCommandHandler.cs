using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class RemoveOwnPasswordCommandHandler(UserAccounts accounts)
    : IRequestHandler<RemoveOwnPasswordCommand, UserRecord>
{
    public Task<UserRecord> Handle(RemoveOwnPasswordCommand request, CancellationToken cancellationToken)
    {
        // There is no password to ask for once it is gone, so only a recent sign-in confirms this.
        accounts.ConfirmOwnChange(request.Actor.Id, request.RecentSignIn, null);
        return Task.FromResult(accounts.RemovePassword(request.Actor, request.Actor.Id));
    }
}
