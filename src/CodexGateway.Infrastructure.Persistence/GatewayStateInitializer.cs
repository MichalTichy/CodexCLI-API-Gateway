using CodexGateway.Logic.Configuration;
using CodexGateway.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Infrastructure.Persistence;

public sealed class GatewayStateInitializer(
    IRepository<GatewayState> repository,
    IOptions<GatewayOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (await repository.GetByIdAsync(GatewayState.DocumentId, cancellationToken) is not null)
        {
            return;
        }

        var apiKeys = (options.Value.ApiKeys ?? [])
            .Select(key => new GatewayApiKeyDefinition
            {
                Id = key.Id,
                Name = key.Name,
                Key = key.Key
            })
            .ToList();
        Validate(apiKeys);
        await repository.AddAsync(
            new GatewayState { ApiKeys = apiKeys },
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void Validate(IReadOnlyCollection<GatewayApiKeyDefinition> apiKeys)
    {
        if (apiKeys.Any(key =>
                string.IsNullOrWhiteSpace(key.Id)
                || string.IsNullOrWhiteSpace(key.Name)
                || string.IsNullOrWhiteSpace(key.Key))
            || apiKeys.Select(key => key.Id).Distinct(StringComparer.Ordinal).Count() != apiKeys.Count
            || apiKeys.Select(key => key.Key).Distinct(StringComparer.Ordinal).Count() != apiKeys.Count)
        {
            throw new InvalidOperationException(
                "Configured API key IDs and secrets must be non-empty and unique.");
        }
    }
}
