using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex;

public sealed record McpToolMetadata(
    string Name,
    string? Title,
    string? Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    JsonElement? Annotations,
    JsonElement? Icons,
    JsonElement? Meta);
