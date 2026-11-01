using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Authentication;
using Namerre.Infrastructure.Configuration;
using Namerre.Notifications;

namespace Namerre;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the web host's own services: forwarded headers, live notifications, controllers,
    /// authentication, data protection and OpenAPI.
    /// </summary>
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<NamerreOptions>>((options, settings) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                foreach (var proxy in settings.Value.ReverseProxy.KnownProxies)
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                foreach (var network in settings.Value.ReverseProxy.KnownNetworks)
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            });
        services.AddSignalR();
        services.AddSingleton<IRenameNotifier, HubRenameNotifier>();
        services.AddControllersWithViews();
        services.AddUserAuthentication();
        // Keys that protect session and antiforgery cookies live with the database, so sessions survive restarts and
        // container replacement instead of depending on wherever the default key location happens to be.
        services.AddDataProtection().SetApplicationName("Namerre");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<NamerreOptions>, IHostEnvironment, ILoggerFactory>((options, settings, environment, loggers) =>
            {
                if (string.IsNullOrWhiteSpace(settings.Value.DataPath))
                    return;
                var keys = Path.Combine(Path.GetFullPath(settings.Value.DataPath, environment.ContentRootPath), "keys");
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keys), loggers);
            });
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        services.AddOpenApi();

        return services;
    }
}
