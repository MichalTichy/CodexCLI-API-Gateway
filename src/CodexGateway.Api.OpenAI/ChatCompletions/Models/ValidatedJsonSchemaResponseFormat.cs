using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGateway.Api.OpenAI;

public sealed record ValidatedJsonSchemaResponseFormat(
    string Name,
    string? Description,
    JsonElement Schema);
