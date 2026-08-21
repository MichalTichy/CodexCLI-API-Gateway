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

    public Task<IReadOnlyList<ResolvedMcpServer>> ResolveVisibleAsync(
        ProjectApiKeyAccess? access,
        CancellationToken cancellationToken) =>
        repository.QueryAsync(
            new VisibleMcpServersSpecification(access),
            cancellationToken);

    private abstract class McpServersSpecification(ProjectApiKeyAccess? access)
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

                var selected = SelectTools(assignment)
                    .Where(tool => (definition.AvailableTools ?? []).Contains(tool, StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                result.Add(new ResolvedMcpServer(definition, selected, assignment.Required));
            }

            return result;
        }

        protected abstract IEnumerable<string> SelectTools(ProjectMcpAssignment assignment);
    }

    private sealed class EnabledMcpServersSpecification(ProjectApiKeyAccess? access)
        : McpServersSpecification(access)
    {
        protected override IEnumerable<string> SelectTools(ProjectMcpAssignment assignment) =>
            assignment.EnabledTools ?? [];
    }

    private sealed class VisibleMcpServersSpecification(ProjectApiKeyAccess? access)
        : McpServersSpecification(access)
    {
        protected override IEnumerable<string> SelectTools(ProjectMcpAssignment assignment) =>
            assignment.VisibleTools ?? assignment.EnabledTools ?? [];
    }
}
