using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Api.OpenAI.ChatCompletions.Models;

public sealed class ChatResponseFormat
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("json_schema")]
    public ChatJsonSchemaFormat? JsonSchema { get; set; }
}
