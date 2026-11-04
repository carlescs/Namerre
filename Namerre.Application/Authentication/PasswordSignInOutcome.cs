namespace Namerre.Application.Authentication;

public enum PasswordSignInOutcome
{
    SignedIn,
    /// <summary>The username and password do not match an enabled user.</summary>
    Invalid,
    /// <summary>Refused without checking the password: the account is being guessed at too quickly.</summary>
    Throttled,
    /// <summary>The password is right, but administrators must sign in with a passkey (or single sign-on).</summary>
    PasskeyRequired
}
