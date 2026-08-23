using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex;

public sealed record ResolvedMcpServer(
    McpServerDefinition Definition,
    IReadOnlyList<string> EnabledTools,
    bool Required);
