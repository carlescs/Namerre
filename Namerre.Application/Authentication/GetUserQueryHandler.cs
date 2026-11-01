using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class GetUserQueryHandler(UserAccounts accounts) : IRequestHandler<GetUserQuery, UserRecord?>
{
    public Task<UserRecord?> Handle(GetUserQuery request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.Find(request.Id));
}
