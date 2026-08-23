using CodexGateway.IoC;
using CodexGateway.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.Persistence.Marten.Extensions;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Infrastructure.Persistence.Composition;

public sealed class PersistenceInfrastructureInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<InMemoryGatewayStateRepository>();
            services.AddSingleton<IRepository<GatewayState>>(provider =>
                provider.GetRequiredService<InMemoryGatewayStateRepository>());
            services.AddSingleton<IReadOnlyRepository<GatewayState>>(provider =>
                provider.GetRequiredService<InMemoryGatewayStateRepository>());
        }
        else
        {
            var connectionString = configuration.GetConnectionString("Gateway")
                ?? throw new InvalidOperationException(
                    "The ConnectionStrings:Gateway PostgreSQL connection string is required.");
            services.AddMartenPostgresPersistence(connectionString, options =>
            {
                options.Schema.For<GatewayState>().UseOptimisticConcurrency(true);
            });
        }

        services.AddHostedService<GatewayStateInitializer>();
    }
}
