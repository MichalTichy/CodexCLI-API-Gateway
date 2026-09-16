using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex.Models;

public sealed record CodexRunRequest(
    string Prompt,
    string Model,
    string ReasoningEffort,
    RunWorkspace Workspace,
    IReadOnlyList<ResolvedMcpServer> McpServers,
    WebSearchMode WebSearchMode = WebSearchMode.Disabled,
    JsonElement? OutputSchema = null,
    string? RunnerImage = null);
