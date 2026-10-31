using CMediator;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Plugins;

namespace Namerre.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddInProcessMediator();
        services.AddGeneratedMediator();
        // Registration order is the catalog order users see; execution order comes from each request.
        services.AddSingleton<IRenamePlugin, LiteralPlugin>();
        services.AddSingleton<IRenamePlugin, RegexPlugin>();
        services.AddSingleton<IRenamePlugin, CasePlugin>();
        services.AddSingleton<IRenamePlugin, AffixPlugin>();
        services.AddSingleton<IRenamePlugin, PadNumbersPlugin>();
        services.AddSingleton<IRenamePlugin, CounterPlugin>();
        services.AddSingleton<IRenamePlugin, DatePlugin>();
        services.AddSingleton<IRenamePlugin, TmdbPlugin>();
        services.AddSingleton<PipelineEngine>();

        return services;
    }
}
