using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications;

public sealed class ApiKeyDefinitionsSpecification
    : ISpecification<GatewayState, IReadOnlyList<ApiKeyDefinition>>
{
    public Task<IReadOnlyList<ApiKeyDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ApiKeyDefinition> result = queryable.SingleOrDefault()?.ApiKeys ?? [];
        return Task.FromResult<IReadOnlyList<ApiKeyDefinition>?>(result);
    }
}
