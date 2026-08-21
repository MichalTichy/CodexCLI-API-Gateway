using System.Text.Json;

namespace CodexGateway.Logic.Generation;

public sealed record GenerationUsage(
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens,
    int ReasoningOutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
