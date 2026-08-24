using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Logic.Codex.Models;

public sealed record McpToolAnnotations
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("readOnlyHint")]
    public bool? ReadOnlyHint { get; init; }

    [JsonPropertyName("destructiveHint")]
    public bool? DestructiveHint { get; init; }

    [JsonPropertyName("idempotentHint")]
    public bool? IdempotentHint { get; init; }

    [JsonPropertyName("openWorldHint")]
    public bool? OpenWorldHint { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
