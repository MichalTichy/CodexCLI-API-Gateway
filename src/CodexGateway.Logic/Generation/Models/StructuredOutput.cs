using System.Text.Json;

namespace CodexGateway.Logic.Generation;

public sealed record StructuredOutput(
    string? Name,
    string? Description,
    JsonElement Schema);
