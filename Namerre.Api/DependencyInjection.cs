using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Authentication;
using Namerre.Infrastructure.Configuration;
using Namerre.Notifications;
using Namerre.Security;

namespace Namerre;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the web host's own services: forwarded headers, live notifications, controllers,
    /// authentication, data protection (optionally encrypted with a certificate) and OpenAPI.
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
        services.TryAddSingleton(TimeProvider.System);
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
        // Optionally encrypt the key ring at rest with a certificate. Keys written before a certificate was configured
        // stay readable (ASP.NET Core only decrypts keys that carry an encrypted secret); new keys are encrypted.
        services.AddSingleton<KeyRingCertificates>();
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<NamerreOptions>, KeyRingCertificates>((options, settings, certificates) =>
            {
                if (!string.IsNullOrWhiteSpace(settings.Value.DataProtection.Certificate))
                    options.XmlEncryptor = new CertificateKeyEncryptor(certificates);
            });
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        services.AddOpenApi();

        return services;
    }
}
