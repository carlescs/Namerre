using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class RemoveOwnPasskeyCommandHandler(UserAccounts accounts)
    : IRequestHandler<RemoveOwnPasskeyCommand, UserRecord>
{
    public Task<UserRecord> Handle(RemoveOwnPasskeyCommand request, CancellationToken cancellationToken)
    {
        // Removing a sign-in method ends the user's other sessions, so a stolen old session must not be able to.
        accounts.ConfirmOwnChange(request.Actor.Id, request.RecentSignIn, null);
        return Task.FromResult(accounts.RemovePasskey(request.Actor, request.Actor.Id, request.PasskeyId));
    }
}
