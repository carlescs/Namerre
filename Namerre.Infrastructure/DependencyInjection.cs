using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Application.Folders;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Authentication;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Folders;
using Namerre.Infrastructure.Renaming;

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
            .Bind(configuration.GetSection(NamerreOptions.SectionName));
        services.AddSingleton(provider => new BoundedFileSystem(
            provider.GetRequiredService<IOptions<NamerreOptions>>().Value.RootPath,
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath));
        services.AddSingleton<IRenameFileSystem>(provider => provider.GetRequiredService<BoundedFileSystem>());
        services.AddSingleton(provider => new RenameStore(
            provider.GetRequiredService<BoundedFileSystem>(),
            provider.GetRequiredService<IOptions<NamerreOptions>>().Value.DataPath,
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath));
        services.AddSingleton<IAdministratorStore, AdministratorStore>();
        // TmdbMetadata holds the lookup cache and concurrency limit, so one instance is shared; it keeps its client
        // for the process lifetime and relies on pooled connection recycling instead of handler rotation for DNS changes.
        services.AddHttpClient<TmdbMetadata>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        services.AddSingleton<ITmdbMetadata>(provider => provider.GetRequiredService<TmdbMetadata>());
        services.AddSingleton<RenameService>();
        services.AddSingleton<IRenameService>(provider => provider.GetRequiredService<RenameService>());
        services.AddHostedService<RenameWorker>();
        services.AddSingleton<IFolderBrowser>(provider => new ServerFolderBrowser(
            provider.GetRequiredService<IOptions<NamerreOptions>>().Value.RootPath,
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath,
            provider.GetRequiredService<RenameStore>().IsOwnedEntry));

        return services;
    }
}
