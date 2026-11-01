using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class VerifyPasswordQueryHandler(UserAccounts accounts) : IRequestHandler<VerifyPasswordQuery, bool>
{
    public Task<bool> Handle(VerifyPasswordQuery request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.Find(request.UserId) is { } user && accounts.VerifyPassword(user, request.Password));
}
