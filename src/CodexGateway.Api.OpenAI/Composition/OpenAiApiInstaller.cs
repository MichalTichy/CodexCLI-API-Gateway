using CodexGateway.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Api.OpenAI.Composition;

public sealed class OpenAiApiInstaller : IInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<OpenAiChatCompletionMapper>();
        services.AddSingleton(new GatewayEndpointAssembly(typeof(OpenAiApiInstaller).Assembly));
    }
}
