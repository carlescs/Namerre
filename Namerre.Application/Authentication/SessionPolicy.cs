namespace Namerre.Application.Authentication;

/// <summary>Fixed rules for sign-in sessions.</summary>
public static class SessionPolicy
{
    /// <summary>How long a session cookie lasts; a revoked session only needs remembering this long.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
}
