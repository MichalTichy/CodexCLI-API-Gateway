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
            .Validate(
                options => options.Models.Length > 0 &&
                           options.Models.All(model =>
                               !string.IsNullOrWhiteSpace(model.Id) &&
                               !string.IsNullOrWhiteSpace(model.Name) &&
                               model.SupportedReasoningEfforts.Length > 0 &&
                               model.SupportedReasoningEfforts.All(effort => !string.IsNullOrWhiteSpace(effort)) &&
                               model.SupportedReasoningEfforts.Contains(model.DefaultReasoningEffort, StringComparer.Ordinal)) &&
                           options.Models.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count() == options.Models.Length,
                "Codex models must have unique IDs, names, reasoning efforts, and a supported default reasoning effort.")
            .ValidateOnStart();

        services.TryAddSingleton<ContainerRuntime>();
        services.TryAddSingleton<ContainerCodexRunner>();
        services.TryAddSingleton<ICodexRunner>(provider =>
            provider.GetRequiredService<ContainerCodexRunner>());

        services.TryAddSingleton<McpMetadataDiscoveryService>();
        services.TryAddSingleton<IMcpMetadataDiscoveryService>(provider =>
            provider.GetRequiredService<McpMetadataDiscoveryService>());

        services.TryAddSingleton<ConfiguredCodexModelCatalog>();
        services.TryAddSingleton<ICodexModelCatalog>(provider =>
            provider.GetRequiredService<ConfiguredCodexModelCatalog>());

        services.TryAddSingleton<HostCodexAuthenticationManager>();
        services.TryAddSingleton<ICodexAuthenticationManager>(provider =>
            provider.GetRequiredService<HostCodexAuthenticationManager>());
    }
}
