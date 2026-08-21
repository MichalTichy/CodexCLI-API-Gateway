using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Api.OpenAI;

public sealed class ChatJsonSchemaFormat
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; }

    [JsonPropertyName("schema")]
    public JsonElement Schema { get; set; }
}
