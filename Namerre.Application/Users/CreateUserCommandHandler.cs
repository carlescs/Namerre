using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class CreateUserCommandHandler(UserAccounts accounts) : IRequestHandler<CreateUserCommand, UserRecord>
{
    public Task<UserRecord> Handle(CreateUserCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.Create(request.Actor, request.Username, request.Role, request.Password,
            request.Login));
}
