using CodexGateway.Models.McpServers;

namespace CodexGateway.McpGateway.Transport;

public interface IStdioMcpUpstreamFactory
{
    Task<IGatewayMcpUpstream> StartAsync(
        StdioMcpServerDefinition definition,
        string workspacePath,
        CancellationToken cancellationToken);
}
