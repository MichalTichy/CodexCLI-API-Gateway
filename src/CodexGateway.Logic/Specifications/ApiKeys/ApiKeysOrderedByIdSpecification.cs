using CodexGateway.Models;
using CodexGateway.Logic.Security;
using Marten;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.ApiKeys;

public sealed class ApiKeysOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<ApiKeyIdentity>>
{
    public async Task<IReadOnlyList<ApiKeyIdentity>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        var apiKeys = await queryable
            .SelectMany(state => state.ApiKeys)
            .Select(key => new ApiKeyIdentity(key.Id, key.Name))
            .ToListAsync(cancellationToken);
        return apiKeys
            .OrderBy(key => key.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
