using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Api.OpenAI;

public sealed class ChatStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool? IncludeUsage { get; set; }
}
