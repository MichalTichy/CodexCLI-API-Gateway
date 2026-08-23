using CodexGateway.Models;
using CodexGateway.Logic.Security;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications;

public sealed class ApiKeysOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<ApiKeyIdentity>>
{
    public Task<IReadOnlyList<ApiKeyIdentity>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ApiKeyIdentity> result = (queryable.SingleOrDefault()?.ApiKeys ?? [])
            .Select(key => new ApiKeyIdentity(key.Id, key.Name))
            .OrderBy(key => key.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ApiKeyIdentity>?>(result);
    }
}
