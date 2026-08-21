using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;

namespace CodexGateway.Logic.McpServers;

public sealed class McpServerResolver(IGatewayConfigurationRepository repository)
{
    public Task<IReadOnlyList<ResolvedMcpServer>> ResolveAsync(
        ProjectApiKeyAccess? access,
        CancellationToken cancellationToken) =>
        repository.QueryAsync(
            new EnabledMcpServersSpecification(access),
            cancellationToken);

    private sealed class EnabledMcpServersSpecification(ProjectApiKeyAccess? access)
        : ISpecification<GatewayState, IReadOnlyList<ResolvedMcpServer>>
    {
        public IReadOnlyList<ResolvedMcpServer> Apply(GatewayState source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (access is null)
            {
                return [];
            }

            var result = new List<ResolvedMcpServer>();
            foreach (var assignment in (access.McpServers ?? []).OfType<ProjectMcpAssignment>())
            {
                var definition = source.McpServers.SingleOrDefault(server => server.Id == assignment.ServerId);
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
}
