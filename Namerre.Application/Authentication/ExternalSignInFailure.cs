namespace Namerre.Application.Authentication;

/// <summary>Why a single sign-on identity could not sign in.</summary>
public enum ExternalSignInFailure
{
    None,
    Disabled,
    NotLinked,
    /// <summary>No administrator exists yet; the installation token must create one first.</summary>
    SetupRequired,
    Unavailable
}
