using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications;

public sealed class ApiKeyDefinitionsSpecification
    : ISpecification<GatewayState, IReadOnlyList<GatewayApiKeyDefinition>>
{
    public Task<IReadOnlyList<GatewayApiKeyDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GatewayApiKeyDefinition> result = queryable.SingleOrDefault()?.ApiKeys ?? [];
        return Task.FromResult<IReadOnlyList<GatewayApiKeyDefinition>?>(result);
    }
}
