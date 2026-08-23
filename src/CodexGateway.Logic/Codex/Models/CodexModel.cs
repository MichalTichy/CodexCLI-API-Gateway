using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex.Models;

public sealed record CodexModel(
    string Id,
    string Name,
    IReadOnlyList<string> SupportedReasoningEfforts,
    string DefaultReasoningEffort);
