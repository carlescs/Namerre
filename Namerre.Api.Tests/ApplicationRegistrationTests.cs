using CMediator.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Namerre.Application;
using Namerre.Application.Folders;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Presets;
using Namerre.Application.WatchFolders;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Folders;
using Namerre.Infrastructure.Renaming;
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
    public void IntegrationSettingsBindAndTheNotifierIsRegistered()
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Namerre:Integrations:Plex:Url"] = "http://plex:32400",
                    ["Namerre:Integrations:Plex:Token"] = "plex-token",
                    ["Namerre:Integrations:Webhook:Url"] = "https://hooks.example/run",
                    ["Namerre:Integrations:Webhook:IncludePaths"] = "true"
                })));

        var integrations = factory.Services.GetRequiredService<IOptions<NamerreOptions>>().Value.Integrations;

        Assert.Equal("http://plex:32400", integrations.Plex.Url);
        Assert.Equal("plex-token", integrations.Plex.Token);
        Assert.True(integrations.Webhook.IncludePaths);
        Assert.Null(integrations.Jellyfin.Url);
        Assert.NotNull(factory.Services.GetRequiredService<ILibraryNotifier>());
    }

    [Fact]
    public void InvalidIntegrationSettingsFailStartup()
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Namerre:Integrations:Jellyfin:Url"] = "http://jelly:8096"
                })));

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);
        var failure = exception as OptionsValidationException ?? exception.InnerException as OptionsValidationException;
        Assert.NotNull(failure);
        Assert.Contains("Namerre:Integrations:Jellyfin:ApiKey", failure.Message);
    }

    [Fact]
    public void InvalidWatchFoldersFailStartup()
    {
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Namerre:WatchFolders:0:Name"] = "Downloads",
                    ["Namerre:WatchFolders:0:Path"] = "downloads",
                    ["Namerre:WatchFolders:0:Preset"] = "TV",
                    ["Namerre:WatchFolders:0:TmdbAutoConfirm"] = "0.9"
                })));

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);
        var failure = exception as OptionsValidationException ?? exception.InnerException as OptionsValidationException;
        Assert.NotNull(failure);
        Assert.Contains("TmdbAutoConfirm is 0.5–1 and needs TmdbMediaType", failure.Message);
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
        services.AddSingleton<IWatchFolderConfiguration>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        services.AddSingleton<IWatchedFileStore>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        services.AddSingleton<IWatchFolderDiagnostics>(_ => throw new InvalidOperationException("Not used by sender lifetime test."));
        services.AddLogging();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        Assert.Same(sender, scope.ServiceProvider.GetRequiredService<ISender>());

        using var otherScope = provider.CreateScope();
        Assert.NotSame(sender, otherScope.ServiceProvider.GetRequiredService<ISender>());
    }

    [Fact]
    public void TestPipelineMatchesTheRegisteredCatalog()
    {
        var fileSystem = new BoundedFileSystem(null, Path.GetTempPath());
        var metadata = new UnusedMetadata();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<ITmdbMetadata>(metadata);
        services.AddSingleton<IRenameFileSystem>(fileSystem);
        using var provider = services.BuildServiceProvider();

        var registered = provider.GetRequiredService<PipelineEngine>().Catalog.Select(p => (p.TypeId, p.Version));
        var helper = TestPipeline.Create(metadata, fileSystem).Catalog.Select(p => (p.TypeId, p.Version));

        Assert.Equal(registered, helper);
    }

    private sealed class UnusedMetadata : ITmdbMetadata
    {
        public Task<TmdbSearchResult> Search(TmdbSearchRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public TmdbMatch Resolve(MetadataSelection selection) => throw new NotSupportedException();
    }
}
