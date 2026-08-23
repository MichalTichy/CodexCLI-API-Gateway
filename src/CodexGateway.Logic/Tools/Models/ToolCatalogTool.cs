using System.Text.Json;

namespace CodexGateway.Logic.Tools;

public sealed record ToolCatalogTool(
    string ServerId,
    string Name,
    string? Title,
    string? Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    JsonElement? Annotations,
    JsonElement? Icons,
    JsonElement? Meta)
{
    public string Id => ServerId + "/" + Name;
}
