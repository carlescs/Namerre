using CMediator;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Presets;

namespace Namerre.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddInProcessMediator();
        services.AddGeneratedMediator();
        // Registration order is the catalog order users see; execution order comes from each request.
        services.AddSingleton<IRenamePlugin, LiteralPlugin>();
        services.AddSingleton<IRenamePlugin, ReplacementTablePlugin>();
        services.AddSingleton<IRenamePlugin, RegexPlugin>();
        services.AddSingleton<IRenamePlugin, CasePlugin>();
        services.AddSingleton<IRenamePlugin, ExtensionCasePlugin>();
        services.AddSingleton<IRenamePlugin, AffixPlugin>();
        services.AddSingleton<IRenamePlugin, SeparatorsPlugin>();
        services.AddSingleton<IRenamePlugin, ReleaseTagsPlugin>();
        services.AddSingleton<IRenamePlugin, CharactersPlugin>();
        services.AddSingleton<IRenamePlugin, InsertPlugin>();
        services.AddSingleton<IRenamePlugin, RemovePlugin>();
        services.AddSingleton<IRenamePlugin, TruncatePlugin>();
        services.AddSingleton<IRenamePlugin, PadNumbersPlugin>();
        services.AddSingleton<IRenamePlugin, CounterPlugin>();
        services.AddSingleton<IRenamePlugin, DatePlugin>();
        services.AddSingleton<IRenamePlugin, TmdbPlugin>();
        services.AddSingleton<PipelineEngine>();
        services.AddSingleton<TmdbSuggester>();
        services.AddSingleton<PresetService>();

        return services;
    }
}
