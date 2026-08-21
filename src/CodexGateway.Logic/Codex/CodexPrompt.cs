using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public sealed record CodexPrompt(
    string Text,
    IReadOnlyCollection<string> TemporaryFileIds,
    JsonElement? OutputSchema = null);
