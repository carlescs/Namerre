using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class RequiresSetupQueryHandler(UserAccounts accounts) : IRequestHandler<RequiresSetupQuery, bool>
{
    public Task<bool> Handle(RequiresSetupQuery request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.RequiresSetup);
}
