using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex;

public interface IMcpMetadataDiscoveryService
{
    Task<IReadOnlyList<DiscoveredMcpServer>> DiscoverAsync(
        IReadOnlyList<ResolvedMcpServer> servers,
        CancellationToken cancellationToken);
}
