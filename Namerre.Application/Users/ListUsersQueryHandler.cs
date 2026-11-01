using CMediator.Abstractions;
using Namerre.Application.Authentication;

namespace Namerre.Application.Users;

internal sealed class ListUsersQueryHandler(UserAccounts accounts) : IRequestHandler<ListUsersQuery, IReadOnlyList<UserRecord>>
{
    public Task<IReadOnlyList<UserRecord>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.List());
}
