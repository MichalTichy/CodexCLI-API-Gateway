using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex;

public sealed record McpServerInfoMetadata(
    string Name,
    string Version,
    string? Title,
    string? Description,
    string? WebsiteUrl,
    JsonElement? Icons);
