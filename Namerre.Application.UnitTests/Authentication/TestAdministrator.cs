using Namerre.Application.Authentication;

namespace Namerre.Testing;

/// <summary>A configured break-glass administrator, or none when both values are null.</summary>
internal sealed record TestAdministrator(string? Username = null, string? PasswordHash = null)
    : IAdministratorConfiguration;
