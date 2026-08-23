using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Api.OpenAI.Serialization;

public static class OpenAiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
