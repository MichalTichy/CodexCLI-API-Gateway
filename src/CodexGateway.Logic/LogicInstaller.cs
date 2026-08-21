using CodexGateway.IoC;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Files;
using CodexGateway.Logic.Generation;
using CodexGateway.Logic.Models;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Projects;
using CodexGateway.Logic.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Logic;

public sealed class LogicInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<GatewayOptions>()
            .Bind(configuration.GetSection(GatewayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMediatR(options =>
            options.RegisterServicesFromAssemblyContaining<LogicInstaller>());

        services.AddSingleton<GlobalApiKeyService>();
        services.AddHostedService<ApiKeyCatalogInitializer>();
        services.AddSingleton<GatewayAccessService>();
        services.AddSingleton<ProjectAccessResolver>();
        services.AddSingleton<McpServerResolver>();
        services.AddSingleton<RunCoordinator>();
        services.AddSingleton<CodexExecutionService>();
        services.AddSingleton<ModelCatalogService>();
        services.AddSingleton<PromptComposer>();
        services.AddSingleton<GatewayFileService>();
    }
}
