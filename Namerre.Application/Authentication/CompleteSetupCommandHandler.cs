using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class CompleteSetupCommandHandler(UserAccounts accounts)
    : IRequestHandler<CompleteSetupCommand, SetupResult>
{
    public Task<SetupResult> Handle(CompleteSetupCommand request, CancellationToken cancellationToken)
        // An unusable username is reported before the token is checked, so the form can say what to fix.
        => Task.FromResult(UserAccounts.IsValidUsername(request.Username)
            ? accounts.CompleteSetup(request.Token, request.Username, request.Password)
            : SetupResult.InvalidUsername);
}
