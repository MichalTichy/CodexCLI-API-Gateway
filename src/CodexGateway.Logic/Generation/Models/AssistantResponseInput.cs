using System.Text.Json;

namespace CodexGateway.Logic.Generation;

public sealed record AssistantResponseInput(
    GatewayRequestContext Context,
    string ModelId,
    string? ReasoningEffort,
    IReadOnlyList<InputMessage> Messages,
    IReadOnlyCollection<string> FileIds,
    StructuredOutput? StructuredOutput = null);
