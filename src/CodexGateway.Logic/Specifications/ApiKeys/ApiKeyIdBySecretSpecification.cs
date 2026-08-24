using CodexGateway.Models;
using Marten;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.ApiKeys;

public sealed class ApiKeyIdBySecretSpecification(string secret)
    : ISpecification<GatewayState, string>
{
    public Task<string?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        return queryable
            .SelectMany(state => state.ApiKeys)
            .Where(key => key.Key == secret)
            .Select(key => key.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
