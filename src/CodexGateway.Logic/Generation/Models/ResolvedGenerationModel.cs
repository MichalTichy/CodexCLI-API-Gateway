using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;

namespace CodexGateway.Logic.Models;

public sealed record ResolvedGenerationModel(CodexModel Model, string ReasoningEffort);
