using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class RemovePasswordCommandHandler(UserAccounts accounts) : IRequestHandler<RemovePasswordCommand, UserRecord>
{
    public Task<UserRecord> Handle(RemovePasswordCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.RemovePassword(request.Actor, request.UserId));
}
