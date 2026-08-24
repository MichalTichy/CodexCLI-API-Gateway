using CodexGateway.Logic.Codex;

namespace CodexGateway.Logic.McpServers;

public interface IGatewayMcpSessionFactory
{
    Task<GatewayMcpSessionLease> CreateAsync(
        string workspacePath,
        IReadOnlyList<ResolvedMcpServer> servers,
        CancellationToken cancellationToken);
}
