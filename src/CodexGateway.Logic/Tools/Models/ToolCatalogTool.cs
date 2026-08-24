using System.Text.Json;

namespace CodexGateway.Logic.Tools.Models;

public sealed record ToolCatalogTool(
    string ServerId,
    string Name,
    string? Title,
    string? Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    McpToolAnnotations? Annotations,
    IReadOnlyList<McpIcon>? Icons,
    JsonElement? Meta)
{
    public string Id => ServerId + "/" + Name;
}
