namespace Namerre.Application.Authentication;

/// <summary>
/// The break-glass administrator configured on the server (<c>Namerre:Authentication</c>); both values are empty
/// when none is configured.
/// </summary>
public interface IAdministratorConfiguration
{
    string? Username { get; }
    string? PasswordHash { get; }
}
