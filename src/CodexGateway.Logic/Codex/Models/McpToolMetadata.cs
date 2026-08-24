using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex.Models;

public sealed record McpToolMetadata(
    string Name,
    string? Title,
    string? Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    McpToolAnnotations? Annotations,
    IReadOnlyList<McpIcon>? Icons,
    JsonElement? Meta);
