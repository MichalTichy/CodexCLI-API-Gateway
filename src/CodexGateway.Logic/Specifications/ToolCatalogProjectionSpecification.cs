using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Tools;

namespace CodexGateway.Logic.Specifications;

/// <summary>
/// Projects discovered MCP metadata through the caller's visible and enabled
/// grants. Visibility controls planning metadata; enabled tools are invocable.
/// </summary>
public sealed class ToolCatalogProjectionSpecification(
    IReadOnlyList<ResolvedMcpServer> visibleServers,
    IReadOnlyList<ResolvedMcpServer> enabledServers)
    : ISpecification<IReadOnlyList<DiscoveredMcpServer>, ToolCatalog>
{
    public ToolCatalog Apply(IReadOnlyList<DiscoveredMcpServer> discoveredServers)
    {
        ArgumentNullException.ThrowIfNull(discoveredServers);

        var visibleById = visibleServers.ToDictionary(
            server => server.Definition.Id,
            StringComparer.Ordinal);
        var enabledById = enabledServers.ToDictionary(
            server => server.Definition.Id,
            server => server.EnabledTools.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        var servers = discoveredServers
            .Where(server => server.ServerInfo is not null && visibleById.ContainsKey(server.ServerId))
            .OrderBy(server => server.ServerId, StringComparer.Ordinal)
            .Select(server =>
            {
                var visibility = visibleById[server.ServerId];
                var visibleTools = visibility.EnabledTools.ToHashSet(StringComparer.Ordinal);
                var invocableTools = enabledById.GetValueOrDefault(server.ServerId) ?? [];
                var serverInfo = server.ServerInfo!;
                return new ToolCatalogServer(
                    server.ServerId,
                    visibility.Definition.Name,
                    serverInfo.Version,
                    visibility.Required,
                    serverInfo.Title,
                    serverInfo.Description,
                    serverInfo.WebsiteUrl,
                    serverInfo.Icons,
                    server.Tools
                        .Where(tool => visibleTools.Contains(tool.Name))
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
                            tool.Meta,
                            invocableTools.Contains(tool.Name)))
                        .ToArray());
            })
            .ToArray();
        return new ToolCatalog(servers);
    }
}
