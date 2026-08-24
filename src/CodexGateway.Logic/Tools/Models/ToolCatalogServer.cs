namespace CodexGateway.Logic.Tools.Models;

public sealed record ToolCatalogServer(
    string Id,
    string Name,
    string Version,
    bool Required,
    string? Title,
    string? Description,
    string? WebsiteUrl,
    IReadOnlyList<McpIcon>? Icons,
    IReadOnlyList<ToolCatalogTool> Tools);
