using CodexGateway.Models;
using Marten;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.McpServers;

public sealed class McpServersOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<McpServerDefinition>>
{
    public async Task<IReadOnlyList<McpServerDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        var servers = await queryable
            .SelectMany(state => state.McpServers)
            .Select(server => server)
            .ToListAsync(cancellationToken);
        return servers
            .OrderBy(server => server.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
