using CMediator;
using Microsoft.Extensions.DependencyInjection;

namespace Namerre.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddInProcessMediator();
        services.AddGeneratedMediator();

        return services;
    }
}
