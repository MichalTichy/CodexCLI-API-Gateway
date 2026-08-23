using CodexGateway.Models;
using CodexGateway.Logic.Security;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.ApiKeys;

public sealed class ApiKeysOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<ApiKeyIdentity>>
{
    public async Task<IReadOnlyList<ApiKeyIdentity>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        return await queryable
            .SelectMany(state => state.ApiKeys)
            .OrderBy(key => key.Id)
            .Select(key => new ApiKeyIdentity(key.Id, key.Name))
            .ToListAsync(cancellationToken);
    }
}
