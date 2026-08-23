using CodexGateway.Models;
using CodexGateway.Logic.Security;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications;

public sealed class ApiKeysOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<GlobalApiKeyIdentity>>
{
    public Task<IReadOnlyList<GlobalApiKeyIdentity>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GlobalApiKeyIdentity> result = (queryable.SingleOrDefault()?.ApiKeys ?? [])
            .Select(key => new GlobalApiKeyIdentity(key.Id, key.Name))
            .OrderBy(key => key.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<GlobalApiKeyIdentity>?>(result);
    }
}
