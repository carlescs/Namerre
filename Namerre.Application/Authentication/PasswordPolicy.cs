namespace Namerre.Application.Authentication;

/// <summary>The password policy shared by every account.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    // A password needs this many characters besides the username it contains (alice-new-password is fine,
    // alice2024!!! is not).
    private const int MinimumBesidesUsername = 8;
    private const int MinimumDistinctCharacters = 5;

    public static readonly string WeakPasswordMessage =
        $"Use a password of at least {MinimumLength} characters that is not a common or breached password and does not repeat your username.";

    /// <summary>
    /// Applies to new passwords only, so an existing weaker password keeps working until it is changed. Refuses
    /// passwords shorter than <see cref="MinimumLength"/>, with too few distinct characters, on the bundled list of
    /// common and breached passwords (ignoring case and surrounding digits and symbols), or made mostly of the
    /// username.
    /// </summary>
    public static bool IsAcceptable(string password, string? username = null)
        => password.Length >= MinimumLength && !string.IsNullOrWhiteSpace(password) &&
            password.Distinct().Count() >= MinimumDistinctCharacters && !CommonPasswords.Contains(password) &&
            !IsMostlyUsername(password, username);

    private static bool IsMostlyUsername(string password, string? username)
    {
        username = username?.Trim();
        if (string.IsNullOrEmpty(username) || password.IndexOf(username, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        var rest = password.Replace(username, "", StringComparison.OrdinalIgnoreCase);
        return rest.Length < MinimumBesidesUsername;
    }
}
