using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex;

public sealed record DiscoveredMcpServer(
    string ServerId,
    McpServerInfoMetadata? ServerInfo,
    IReadOnlyList<McpToolMetadata> Tools);
