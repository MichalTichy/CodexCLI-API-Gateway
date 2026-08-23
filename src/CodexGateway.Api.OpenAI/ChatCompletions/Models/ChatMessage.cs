using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Api.OpenAI;

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public JsonElement Content { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("tool_calls")]
    public JsonElement? ToolCalls { get; set; }

    [JsonPropertyName("function_call")]
    public JsonElement? FunctionCall { get; set; }
}
