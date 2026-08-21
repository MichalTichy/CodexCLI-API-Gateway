using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Tools;

namespace CodexGateway.Logic.Specifications;

/// <summary>
/// Projects discovered MCP metadata through the caller's enabled tool grants.
/// </summary>
public sealed class ToolCatalogProjectionSpecification(
    IReadOnlyList<ResolvedMcpServer> enabledServers)
    : ISpecification<IReadOnlyList<DiscoveredMcpServer>, ToolCatalog>
{
    public ToolCatalog Apply(IReadOnlyList<DiscoveredMcpServer> discoveredServers)
    {
        ArgumentNullException.ThrowIfNull(discoveredServers);

        var enabledById = enabledServers.ToDictionary(
            server => server.Definition.Id,
            StringComparer.Ordinal);

        var servers = discoveredServers
            .Where(server => server.ServerInfo is not null && enabledById.ContainsKey(server.ServerId))
            .OrderBy(server => server.ServerId, StringComparer.Ordinal)
            .Select(server =>
            {
                var enabled = enabledById[server.ServerId];
                var enabledTools = enabled.EnabledTools.ToHashSet(StringComparer.Ordinal);
                var serverInfo = server.ServerInfo!;
                return new ToolCatalogServer(
                    server.ServerId,
                    enabled.Definition.Name,
                    serverInfo.Version,
                    enabled.Required,
                    serverInfo.Title,
                    serverInfo.Description,
                    serverInfo.WebsiteUrl,
                    serverInfo.Icons,
                    server.Tools
                        .Where(tool => enabledTools.Contains(tool.Name))
                        .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                        .Select(tool => new ToolCatalogTool(
                            server.ServerId,
                            tool.Name,
                            tool.Title,
                            tool.Description,
                            tool.InputSchema,
                            tool.OutputSchema,
                            tool.Annotations,
                            tool.Icons,
                            tool.Meta))
                        .ToArray());
            })
            .ToArray();
        return new ToolCatalog(servers);
    }
}
