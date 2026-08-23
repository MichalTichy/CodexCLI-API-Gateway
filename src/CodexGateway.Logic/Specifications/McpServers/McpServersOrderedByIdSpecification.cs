using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.McpServers;

public sealed class McpServersOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<McpServerDefinition>>
{
    public async Task<IReadOnlyList<McpServerDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        return await queryable
            .SelectMany(state => state.McpServers)
            .OrderBy(server => server.Id)
            .ToListAsync(cancellationToken);
    }
}
