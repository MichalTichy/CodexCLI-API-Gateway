using CodexGateway.Logic.McpServers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.McpGateway.Composition;

public sealed class McpGatewayInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<GatewayMcpOptions>()
            .Bind(configuration.GetSection(GatewayMcpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<GatewayMcpSessionManager>();
        services.TryAddSingleton<IGatewayMcpSessionFactory>(provider =>
            provider.GetRequiredService<GatewayMcpSessionManager>());
        services.TryAddSingleton<IGatewayMcpRequestHandler>(provider =>
            provider.GetRequiredService<GatewayMcpSessionManager>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, GatewayMcpSessionCleanupService>());
    }
}
