namespace Namerre.Application.Authentication;

/// <summary>A stored local user with its single sign-on links.</summary>
/// <param name="PasswordHash">Null when the user only signs in with single sign-on.</param>
/// <param name="Stamp">Random value replaced whenever the user's access changes, which ends existing sessions.</param>
public sealed record UserRecord(long Id, string Username, string? PasswordHash, UserRole Role, bool Disabled,
    string Stamp, DateTimeOffset CreatedAt, DateTimeOffset? LastSignInAt, IReadOnlyList<UserLoginRecord> Logins)
{
    public bool HasPassword => PasswordHash is not null;
    /// <summary>Number of ways this user can sign in.</summary>
    public int SignInMethods => (HasPassword ? 1 : 0) + Logins.Count;
}
