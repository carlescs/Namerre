using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class SetPasswordCommandHandler(UserAccounts accounts) : IRequestHandler<SetPasswordCommand, UserRecord>
{
    public Task<UserRecord> Handle(SetPasswordCommand request, CancellationToken cancellationToken)
        => Task.FromResult(accounts.SetPassword(request.Actor, request.UserId, request.CurrentPassword,
            request.NewPassword, request.RecentSignIn));
}
