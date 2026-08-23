using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex.Models;

public sealed record ResolvedMcpServer(
    McpServerDefinition Definition,
    IReadOnlyList<string> EnabledTools,
    bool Required);
