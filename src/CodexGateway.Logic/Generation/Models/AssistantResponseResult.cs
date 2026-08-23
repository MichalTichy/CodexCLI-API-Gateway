using System.Text.Json;

namespace CodexGateway.Logic.Generation.Models;

public sealed record AssistantResponseResult(
    string ModelId,
    string ReasoningEffort,
    string Text,
    GenerationUsage Usage);
