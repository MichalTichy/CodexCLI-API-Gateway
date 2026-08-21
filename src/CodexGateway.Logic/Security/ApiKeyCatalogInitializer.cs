using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Logic.Security;

public sealed class ApiKeyCatalogInitializer(
    IGatewayConfigurationRepository repository,
    GlobalApiKeyService apiKeys) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) =>
        apiKeys.Replace(await repository.QueryAsync(
            new ApiKeyDefinitionsSpecification(),
            cancellationToken));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
