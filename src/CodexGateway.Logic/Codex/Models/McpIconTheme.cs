using System.Text.Json.Serialization;

namespace CodexGateway.Logic.Codex.Models;

[JsonConverter(typeof(JsonStringEnumConverter<McpIconTheme>))]
public enum McpIconTheme
{
    [JsonStringEnumMemberName("light")]
    Light,

    [JsonStringEnumMemberName("dark")]
    Dark
}
