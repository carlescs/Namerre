using CMediator.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Application;
using Namerre.Application.Folders;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Folders;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public void AddApplicationRegistersAScopedSender()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IFolderBrowser>(new ServerFolderBrowser(null, Path.GetTempPath()));
        services.AddSingleton<IRenameService>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        services.AddSingleton<ITmdbMetadata>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        Assert.Same(sender, scope.ServiceProvider.GetRequiredService<ISender>());

        using var otherScope = provider.CreateScope();
        Assert.NotSame(sender, otherScope.ServiceProvider.GetRequiredService<ISender>());
    }
}
