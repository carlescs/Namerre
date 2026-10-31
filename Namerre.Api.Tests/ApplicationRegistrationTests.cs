using CMediator.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Namerre.Application;
using Namerre.Application.Folders;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Presets;
using Namerre.Infrastructure.Folders;
using Namerre.Infrastructure.Configuration;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public void ApplicationSettingsBindFromTheNamerreSection()
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Namerre:RootPath"] = "media",
                    ["Namerre:Tmdb:ReadAccessToken"] = "test-token",
                    ["Namerre:Tmdb:Language"] = "fr"
                })));

        var options = factory.Services.GetRequiredService<IOptions<NamerreOptions>>().Value;

        Assert.Equal("media", options.RootPath);
        Assert.Equal("", options.DataPath);
        Assert.Equal(AuthWebApplicationFactory.Username, options.Authentication.Username);
        Assert.False(string.IsNullOrEmpty(options.Authentication.PasswordHash));
        Assert.Equal("test-token", options.Tmdb.ReadAccessToken);
        Assert.Equal("fr", options.Tmdb.Language);
    }

    [Fact]
    public void AddApplicationRegistersAScopedSender()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IFolderBrowser>(new ServerFolderBrowser(null, Path.GetTempPath()));
        services.AddSingleton<IRenameService>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        services.AddSingleton<ITmdbMetadata>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        services.AddSingleton<IRenameFileSystem>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        services.AddSingleton<IPresetStore>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        Assert.Same(sender, scope.ServiceProvider.GetRequiredService<ISender>());

        using var otherScope = provider.CreateScope();
        Assert.NotSame(sender, otherScope.ServiceProvider.GetRequiredService<ISender>());
    }
}
