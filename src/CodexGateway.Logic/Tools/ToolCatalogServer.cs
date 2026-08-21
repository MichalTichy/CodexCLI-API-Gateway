using System.Text.Json;

namespace CodexGateway.Logic.Tools;

public sealed record ToolCatalogServer(
    string Id,
    string Name,
    string Version,
    bool Required,
    string? Title,
    string? Description,
    string? WebsiteUrl,
    JsonElement? Icons,
    IReadOnlyList<ToolCatalogTool> Tools);
