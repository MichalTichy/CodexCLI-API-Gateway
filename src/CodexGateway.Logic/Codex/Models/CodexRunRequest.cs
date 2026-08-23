using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public sealed record CodexRunRequest(
    string Prompt,
    string Model,
    string ReasoningEffort,
    RunWorkspace Workspace,
    IReadOnlyList<ResolvedMcpServer> McpServers,
    JsonElement? OutputSchema = null,
    string? RunnerImage = null);
