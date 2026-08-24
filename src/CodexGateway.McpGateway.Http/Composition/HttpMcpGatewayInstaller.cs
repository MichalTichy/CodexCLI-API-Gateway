using CodexGateway.McpGateway.Http.Transport;
using CodexGateway.McpGateway.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.IoC.Installers;

namespace CodexGateway.McpGateway.Http.Composition;

public sealed class HttpMcpGatewayInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHttpClient(HttpMcpUpstreamFactory.HttpClientName);
        services.TryAddSingleton<IHttpMcpUpstreamFactory, HttpMcpUpstreamFactory>();
    }
}
