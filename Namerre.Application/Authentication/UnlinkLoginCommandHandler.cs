using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class UnlinkLoginCommandHandler(UserAccounts accounts) : IRequestHandler<UnlinkLoginCommand, UserRecord>
{
    public Task<UserRecord> Handle(UnlinkLoginCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.Unlink(request.Actor, request.UserId, request.LoginId));
}
