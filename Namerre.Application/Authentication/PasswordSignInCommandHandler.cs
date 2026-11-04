using CMediator.Abstractions;

namespace Namerre.Application.Authentication;

internal sealed class PasswordSignInCommandHandler(UserAccounts accounts, SignInThrottle throttle)
    : IRequestHandler<PasswordSignInCommand, PasswordSignInResult>
{
    public async Task<PasswordSignInResult> Handle(PasswordSignInCommand request, CancellationToken cancellationToken)
    {
        // A device cookie names the user it was issued to; it only helps when signing in as that user.
        var key = request.Device is { } device && accounts.FindByUsername(request.Username)?.Id == device.UserId
            ? SignInThrottle.DeviceKey(device.UserId, device.Id)
            : SignInThrottle.AccountKey(request.Username);
        if (throttle.Reserve(key) is not { } wait)
            return new PasswordSignInResult(PasswordSignInOutcome.Throttled);
        await throttle.Wait(wait, cancellationToken);

        var user = accounts.Authenticate(request.Username, request.Password);
        if (user is null)
        {
            throttle.RecordFailure(key);
            return new PasswordSignInResult(PasswordSignInOutcome.Invalid);
        }
        throttle.RecordSuccess(key);
        if (accounts.RequiresPasskey(user))
            return new PasswordSignInResult(PasswordSignInOutcome.PasskeyRequired);
        accounts.RecordSignIn(user);
        return new PasswordSignInResult(PasswordSignInOutcome.SignedIn, user);
    }
}
