using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class ConfirmOwnChangeCommandHandler(UserAccounts accounts) : IRequestHandler<ConfirmOwnChangeCommand, UserRecord>
{
    public Task<UserRecord> Handle(ConfirmOwnChangeCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.ConfirmOwnChange(request.UserId, request.RecentSignIn, request.Password));
}
