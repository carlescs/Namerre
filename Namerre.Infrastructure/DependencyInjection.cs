using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Application.Folders;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Presets;
using Namerre.Infrastructure.Authentication;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Diagnostics;
using Namerre.Infrastructure.Folders;
using Namerre.Infrastructure.Integrations;
using Namerre.Infrastructure.Renaming;
using Namerre.Infrastructure.WatchFolders;
using Namerre.Application.WatchFolders;

namespace Namerre.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Binds <see cref="NamerreOptions"/> and registers the filesystem, persistence, TMDB and rename job services.
    /// The host still supplies <see cref="IRenameNotifier"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NamerreOptions>()
            .Bind(configuration.GetSection(NamerreOptions.SectionName))
            .ValidateOnStart();
        // Reading the options validates them, so the store (created before the host starts) fails early too.
        services.AddSingleton<IValidateOptions<NamerreOptions>>(new ValidateOptions<NamerreOptions>(null,
            options => options.Limits.ToLimits().IsValid,
            "Namerre:Limits values must be between 1 and their ceilings: MaxFilesPerRun "
            + $"{RenameLimits.CeilingFiles}, MaxStepsPerRun {RenameLimits.CeilingSteps}, "
            + $"MaxQueuedJobs {RenameLimits.CeilingQueuedJobs}, MaxConcurrentPreviews {RenameLimits.CeilingConcurrentPreviews}."));
        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<NamerreOptions>>().Value.Limits.ToLimits());
        services.AddSingleton<IValidateOptions<NamerreOptions>, NamerreOptionsValidator>();
        services.AddSingleton<IValidateOptions<NamerreOptions>, WatchFolderOptionsValidator>();
        services.AddSingleton(provider => new BoundedFileSystem(
            provider.GetRequiredService<IOptions<NamerreOptions>>().Value.RootPath,
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath));
        services.AddSingleton<IRenameFileSystem>(provider => provider.GetRequiredService<BoundedFileSystem>());
        services.AddSingleton(provider => new RenameStore(
            provider.GetRequiredService<BoundedFileSystem>(),
            provider.GetRequiredService<IOptions<NamerreOptions>>().Value.DataPath,
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath,
            maxQueuedJobs: provider.GetRequiredService<RenameLimits>().MaxQueuedJobs));
        services.AddSingleton<IUserStore, UserStore>();
        services.AddSingleton<IPresetStore, PresetStore>();
        // TmdbResilience bounds each lookup (15 s overall); HttpClient's own 100 s default remains only as a backstop.
        services.AddHttpClient<TmdbClient>()
            .AddResilienceHandler(TmdbResilience.PipelineName, TmdbResilience.Configure);
        services.AddSingleton<ITmdbClient>(provider => new CachedTmdbClient(() => provider.GetRequiredService<TmdbClient>()));
        services.AddSingleton<ITmdbMetadata, TmdbMetadata>();
        // The factory's default loggers write request URLs, and a webhook URL may embed a secret.
        services.AddHttpClient(LibraryNotifier.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .RemoveAllLoggers();
        services.AddSingleton<LibraryNotifier>();
        services.AddSingleton<ILibraryNotifier>(provider => provider.GetRequiredService<LibraryNotifier>());
        services.AddHostedService(provider => provider.GetRequiredService<LibraryNotifier>());
        services.AddSingleton<RenameService>();
        services.AddSingleton<IRenameService>(provider => provider.GetRequiredService<RenameService>());
        services.AddSingleton<IUserHistory>(provider => provider.GetRequiredService<RenameService>());
        services.AddHostedService<RenameWorker>();
        // Backlog gauges; only created when telemetry export asks for them.
        services.AddSingleton<RenameStateMetrics>();
        services.AddSingleton<IWatchFolderConfiguration, WatchFolderConfiguration>();
        services.AddSingleton<IWatchedFileStore, WatchedFileStore>();
        services.AddSingleton<IWatchFolderDiagnostics, WatchFolderDiagnostics>();
        services.AddHostedService<WatchFolderWorker>();
        services.AddSingleton<IFolderBrowser>(provider => new ServerFolderBrowser(
            provider.GetRequiredService<IOptions<NamerreOptions>>().Value.RootPath,
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath,
            provider.GetRequiredService<RenameStore>().IsOwnedEntry));

        return services;
    }
}
