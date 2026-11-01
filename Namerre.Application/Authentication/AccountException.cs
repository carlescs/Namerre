namespace Namerre.Application.Authentication;

/// <summary>
/// An account rule refused a change; <see cref="Code"/> and the message are returned to the client with
/// <see cref="Status"/> as the HTTP status.
/// </summary>
public sealed class AccountException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;

    public static AccountException NotFound() => new("UserNotFound", "The user does not exist.", 404);
    public static AccountException Changed() => new("UserChanged",
        "The user changed while you were editing. Reload and try again.", 409);
    public static AccountException Configured() => new("ManagedByConfiguration",
        "This administrator is configured on the server. Change Namerre:Authentication instead.", 409);
    public static AccountException LastSignInMethod() => new("LastSignInMethod",
        "An enabled user needs at least one way to sign in: a password or a linked single sign-on identity.", 409);
    public static AccountException LoginTaken() => new("LoginTaken",
        "This single sign-on identity is already linked to another user.", 409);
    public static AccountException RecentSignIn() => new("RecentSignInRequired",
        "Confirm it is you: sign in again or enter your password, then retry.", 403);
}
