using CMediator;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Application.Renaming;

namespace Namerre.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddInProcessMediator();
        services.AddGeneratedMediator();
        services.AddSingleton<PipelineEngine>();

        return services;
    }
}
