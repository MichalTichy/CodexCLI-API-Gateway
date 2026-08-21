using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Api.OpenAI;

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; set; }

    [JsonPropertyName("file_ids")]
    public List<string> FileIds { get; set; } = [];

    [JsonPropertyName("response_format")]
    public ChatResponseFormat? ResponseFormat { get; set; }

    [JsonPropertyName("tools")]
    public JsonElement? Tools { get; set; }

    [JsonPropertyName("functions")]
    public JsonElement? Functions { get; set; }

    [JsonPropertyName("tool_choice")]
    public JsonElement? ToolChoice { get; set; }

    [JsonPropertyName("function_call")]
    public JsonElement? FunctionCall { get; set; }

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; set; }

    [JsonPropertyName("web_search_options")]
    public JsonElement? WebSearchOptions { get; set; }

    [JsonPropertyName("stream_options")]
    public ChatStreamOptions? StreamOptions { get; set; }
}
