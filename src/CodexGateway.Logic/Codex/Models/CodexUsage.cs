using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex.Models;

public sealed record CodexUsage(int InputTokens, int OutputTokens, int CachedInputTokens, int ReasoningOutputTokens)
{
    public static CodexUsage Empty { get; } = new(0, 0, 0, 0);
}
