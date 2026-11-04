namespace Namerre.Application.Authentication;

/// <param name="User">The signed-in user, set only for <see cref="PasswordSignInOutcome.SignedIn"/>.</param>
public sealed record PasswordSignInResult(PasswordSignInOutcome Outcome, UserRecord? User = null);
