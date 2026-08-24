using CodexGateway.Models;

namespace CodexGateway.Logic.Codex.Models;

public sealed record McpServerInfoMetadata(
    string Name,
    string Version,
    string? Title,
    string? Description,
    string? WebsiteUrl,
    IReadOnlyList<McpIcon>? Icons);
