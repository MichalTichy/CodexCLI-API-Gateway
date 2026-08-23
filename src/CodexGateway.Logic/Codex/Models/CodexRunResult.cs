using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public sealed record CodexRunResult(string Text, CodexUsage Usage);
