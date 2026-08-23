using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.McpServers;

public sealed class EnabledMcpServersSpecification(ProjectApiKeyAccess? access)
    : ISpecification<GatewayState, IReadOnlyList<ResolvedMcpServer>>
{
    public Task<IReadOnlyList<ResolvedMcpServer>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (access is null)
        {
            return Task.FromResult<IReadOnlyList<ResolvedMcpServer>?>([]);
        }

        var catalog = queryable.SingleOrDefault()?.McpServers ?? [];
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

        return Task.FromResult<IReadOnlyList<ResolvedMcpServer>?>(result);
    }
}
