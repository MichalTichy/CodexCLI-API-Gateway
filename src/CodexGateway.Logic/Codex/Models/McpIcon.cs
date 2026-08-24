using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Logic.Codex.Models;

public sealed record McpIcon
{
    [JsonPropertyName("src")]
    public required string Src { get; init; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    [JsonPropertyName("sizes")]
    public IReadOnlyList<string>? Sizes { get; init; }

    [JsonPropertyName("theme")]
    public McpIconTheme? Theme { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
