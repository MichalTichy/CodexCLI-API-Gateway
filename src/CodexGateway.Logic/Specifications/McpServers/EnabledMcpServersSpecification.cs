using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Models;
using Marten;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.McpServers;

public sealed class EnabledMcpServersSpecification(ProjectApiKeyAccess? access)
    : ISpecification<GatewayState, IReadOnlyList<ResolvedMcpServer>>
{
    public async Task<IReadOnlyList<ResolvedMcpServer>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        if (access is null)
        {
            return [];
        }

        var assignedServerIds = (access.McpServers ?? [])
            .Select(assignment => assignment.ServerId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var catalog = await queryable
            .SelectMany(state => state.McpServers)
            .Where(server => assignedServerIds.Contains(server.Id))
            .ToListAsync(cancellationToken);
        var result = new List<ResolvedMcpServer>();
        foreach (var assignment in access.McpServers ?? [])
        {
            var definition = catalog.SingleOrDefault(server => server.Id == assignment.ServerId);
            if (definition is null || !definition.Enabled)
            {
                if (assignment.Required)
                {
                    throw new CodexUnavailableException(
                        $"Required MCP server '{assignment.ServerId}' is unavailable.");
                }

                continue;
            }

            var selected = (assignment.EnabledTools ?? [])
                .Where(tool => (definition.AvailableTools ?? []).Contains(tool, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            result.Add(new ResolvedMcpServer(definition, selected, assignment.Required));
        }

        return result;
    }
}
