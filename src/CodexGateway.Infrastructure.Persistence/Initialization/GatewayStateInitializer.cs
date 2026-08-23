using CodexGateway.Models;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Infrastructure.Persistence.Initialization;

public sealed class GatewayStateInitializer(IRepository<GatewayState> repository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (await repository.GetByIdAsync(GatewayState.DocumentId, cancellationToken) is not null)
        {
            return;
        }

        await repository.AddAsync(new GatewayState(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
