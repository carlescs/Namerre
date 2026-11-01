namespace Namerre.Application.Authentication;

/// <summary>The password policy shared by every account.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public static readonly string WeakPasswordMessage = $"Use a password of at least {MinimumLength} characters.";

    /// <summary>Applies to new passwords only, so an existing shorter password keeps working until it is changed.</summary>
    public static bool IsAcceptable(string password)
        => password.Length >= MinimumLength && !string.IsNullOrWhiteSpace(password);
}
