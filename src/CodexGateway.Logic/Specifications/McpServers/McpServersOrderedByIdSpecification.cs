using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.McpServers;

public sealed class McpServersOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<McpServerDefinition>>
{
    public Task<IReadOnlyList<McpServerDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<McpServerDefinition> result = (queryable.SingleOrDefault()?.McpServers ?? [])
            .OrderBy(server => server.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<McpServerDefinition>?>(result);
    }
}
