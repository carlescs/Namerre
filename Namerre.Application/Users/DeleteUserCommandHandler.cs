using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class DeleteUserCommandHandler(UserAccounts accounts) : IRequestHandler<DeleteUserCommand, UserRecord>
{
    public Task<UserRecord> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.Delete(request.Actor, request.UserId));
}
