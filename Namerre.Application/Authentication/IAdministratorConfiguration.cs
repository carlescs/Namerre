namespace Namerre.Application.Authentication;

/// <summary>
/// Administrator settings from server configuration (<c>Namerre:Authentication</c>): the break-glass administrator,
/// whose username and password hash are both empty when none is configured, and how administrators sign in.
/// </summary>
public interface IAdministratorConfiguration
{
    string? Username { get; }
    string? PasswordHash { get; }

    /// <summary>
    /// Stored administrators cannot sign in with a password alone; they use a passkey or single sign-on. The
    /// break-glass administrator is exempt, since it cannot have passkeys.
    /// </summary>
    bool AdministratorsRequirePasskey => false;
}
