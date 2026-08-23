using CodexGateway.IoC;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Infrastructure.Codex;

public sealed class CodexInfrastructureInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<CodexOptions>()
            .Bind(configuration.GetSection(CodexOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<ContainerRuntime>();
        services.TryAddSingleton<ContainerCodexRunner>();
        services.TryAddSingleton<ICodexRunner>(provider =>
            provider.GetRequiredService<ContainerCodexRunner>());

        services.TryAddSingleton<McpMetadataDiscoveryService>();
        services.TryAddSingleton<IMcpMetadataDiscoveryService>(provider =>
            provider.GetRequiredService<McpMetadataDiscoveryService>());

        services.TryAddSingleton<CodexAppServerClient>();
        services.TryAddSingleton<ICodexControlPlane>(provider =>
            provider.GetRequiredService<CodexAppServerClient>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ContainerRuntimePreflightService>());
    }
}
