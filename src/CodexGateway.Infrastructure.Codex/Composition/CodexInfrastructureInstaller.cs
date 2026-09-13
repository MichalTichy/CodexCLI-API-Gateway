using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Infrastructure.Codex.Authentication;
using CodexGateway.Infrastructure.Codex.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Infrastructure.Codex.Composition;

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

        services.TryAddSingleton<CodexModelCatalog>();
        services.TryAddSingleton<ICodexModelCatalog>(provider =>
            provider.GetRequiredService<CodexModelCatalog>());

        services.TryAddSingleton<HostCodexAuthenticationManager>();
        services.TryAddSingleton<ICodexAuthenticationManager>(provider =>
            provider.GetRequiredService<HostCodexAuthenticationManager>());
    }
}
