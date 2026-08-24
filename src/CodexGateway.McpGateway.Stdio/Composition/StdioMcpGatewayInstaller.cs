using CodexGateway.McpGateway.Stdio.Transport;
using CodexGateway.McpGateway.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.IoC.Installers;

namespace CodexGateway.McpGateway.Stdio.Composition;

public sealed class StdioMcpGatewayInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.TryAddSingleton<IStdioMcpUpstreamFactory, StdioMcpUpstreamFactory>();
    }
}
