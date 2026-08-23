using CodexGateway.IoC;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Generation;
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

        services.AddSingleton<RunCoordinator>();
        services.AddSingleton<PromptComposer>();
    }
}
