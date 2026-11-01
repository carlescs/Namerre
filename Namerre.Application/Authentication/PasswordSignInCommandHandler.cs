using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class PasswordSignInCommandHandler(UserAccounts accounts)
    : IRequestHandler<PasswordSignInCommand, UserRecord?>
{
    public Task<UserRecord?> Handle(PasswordSignInCommand request, CancellationToken cancellationToken)
    {
        var user = accounts.Authenticate(request.Username, request.Password);
        if (user is not null)
            accounts.RecordSignIn(user);
        return Task.FromResult(user);
    }
}
