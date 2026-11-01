using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class MoveLoginCommandHandler(UserAccounts accounts) : IRequestHandler<MoveLoginCommand, UserRecord>
{
    public Task<UserRecord> Handle(MoveLoginCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.MoveLogin(request.Actor, request.LoginId, request.ToUserId, request.DeleteSource));
}
