using Microsoft.Extensions.Logging;

namespace Namerre.Application.Authentication;

/// <summary>
/// Passkey registration and sign-in: runs the WebAuthn ceremonies (<see cref="IPasskeyCeremonies"/>) and applies the
/// account rules of <see cref="UserAccounts"/>. Registering adds a sign-in method, so it needs the same confirmation
/// as linking single sign-on; signing in requires user verification, so it counts as a fresh sign-in.
/// </summary>
public sealed class PasskeyAccounts(UserAccounts accounts, IPasskeyCeremonies ceremonies,
    ILogger<PasskeyAccounts> logger)
{
    public PasskeyRegistration BeginRegistration(long userId, bool recentSignIn, string? password,
        PasskeyRelyingParty relyingParty)
    {
        var user = accounts.ConfirmOwnChange(userId, recentSignIn, password);
        if (user.Passkeys.Count >= UserAccounts.MaximumPasskeysPerUser)
            throw new AccountException("TooManyPasskeys",
                $"A user can have at most {UserAccounts.MaximumPasskeysPerUser} passkeys. Remove one first.", 409);
        return new PasskeyRegistration(user,
            ceremonies.BeginRegistration(relyingParty, user, UserAccounts.PasskeyUserHandle(user)));
    }

    /// <summary>Verifies the browser's answer and registers the passkey to the user version that started it.</summary>
    public async Task<UserRecord> CompleteRegistration(UserReference actor, string expectedStamp, string state,
        string responseJson, string? name, CancellationToken cancellationToken)
    {
        NewPasskey passkey;
        try
        {
            passkey = await ceremonies.CompleteRegistration(state, responseJson, cancellationToken);
        }
        catch (PasskeyVerificationException exception)
        {
            logger.LogWarning("A passkey registration of user {UserId} failed verification: {Reason}", actor.Id,
                LogText.Clean(exception.Message));
            throw new AccountException("PasskeyRejected", "The passkey could not be verified. Try again.");
        }
        return accounts.AddPasskey(actor, actor.Id, expectedStamp, passkey, name);
    }

    public PasskeyChallenge BeginSignIn(PasskeyRelyingParty relyingParty) => ceremonies.BeginAssertion(relyingParty);

    /// <summary>
    /// Returns the enabled user whose passkey signed the challenge, or null. Unknown passkeys, disabled users and
    /// failed verification are all just null, like a wrong password.
    /// </summary>
    public async Task<UserRecord?> SignIn(string state, string responseJson, CancellationToken cancellationToken)
    {
        var credentialId = ceremonies.AssertedCredentialId(responseJson);
        if (credentialId is null || accounts.FindPasskey(credentialId) is not { } found || found.User.Disabled)
            return null;
        uint signCount;
        try
        {
            signCount = await ceremonies.CompleteAssertion(state, responseJson, found.Passkey, cancellationToken);
        }
        catch (PasskeyVerificationException exception)
        {
            logger.LogWarning("A passkey sign-in of user {UserId} failed verification: {Reason}", found.User.Id,
                LogText.Clean(exception.Message));
            return null;
        }
        // The user may have been disabled, or the passkey removed, while the answer was verified.
        if (accounts.Find(found.User.Id) is not { Disabled: false } user ||
            user.Passkeys.All(p => p.Id != found.Passkey.Id))
            return null;
        accounts.RecordPasskeySignIn(user, found.Passkey, signCount);
        return accounts.Find(user.Id) ?? user;
    }
}
