using CodexGateway.Models;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Infrastructure.Persistence.Initialization;

public sealed class GatewayStateInitializer(IRepository<GatewayState> repository) : InitializerBase
{
    public override int Priority => int.MaxValue;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => true;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(GatewayState.DocumentId) is not null)
        {
            return;
        }

        await repository.AddAsync(new GatewayState());
    }
}
