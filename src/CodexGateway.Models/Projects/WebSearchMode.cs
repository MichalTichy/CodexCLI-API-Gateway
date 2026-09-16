using System.Text.Json.Serialization;

namespace CodexGateway.Models.Projects;

[JsonConverter(typeof(JsonStringEnumConverter<WebSearchMode>))]
public enum WebSearchMode
{
    Disabled,
    Cached,
    Indexed,
    Live
}
