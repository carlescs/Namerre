using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class UpdateUserCommandHandler(UserAccounts accounts) : IRequestHandler<UpdateUserCommand, UserRecord>
{
    public Task<UserRecord> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.Update(request.Actor, request.UserId, request.Role, request.Disabled));
}
