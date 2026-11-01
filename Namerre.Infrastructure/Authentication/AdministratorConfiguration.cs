using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Infrastructure.Authentication;

/// <summary><see cref="IAdministratorConfiguration"/> over <c>Namerre:Authentication</c>.</summary>
public sealed class AdministratorConfiguration(IOptions<NamerreOptions> options) : IAdministratorConfiguration
{
    public string? Username => options.Value.Authentication.Username;
    public string? PasswordHash => options.Value.Authentication.PasswordHash;
}
