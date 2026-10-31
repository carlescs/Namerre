using Microsoft.Extensions.Options;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Infrastructure.WatchFolders;

/// <summary>Fails startup with the first problem of <c>Namerre:WatchFolders</c>.</summary>
public sealed class WatchFolderOptionsValidator : IValidateOptions<NamerreOptions>
{
    public ValidateOptionsResult Validate(string? name, NamerreOptions options)
        => WatchFolderOptions.Problem(options.WatchFolders) is { } problem
            ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
