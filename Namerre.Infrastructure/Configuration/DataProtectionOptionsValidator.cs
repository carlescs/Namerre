using Microsoft.Extensions.Options;

namespace Namerre.Infrastructure.Configuration;

/// <summary>Fails startup when the key ring certificate settings cannot be used. Messages never include passwords.</summary>
public sealed class DataProtectionOptionsValidator : IValidateOptions<NamerreOptions>
{
    public ValidateOptionsResult Validate(string? name, NamerreOptions options)
        => DataProtectionCertificates.Problem(options.DataProtection) is { } problem
            ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
