using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;

namespace CodexGateway.Logic.Generation.Models;

public sealed record ResolvedGenerationModel(CodexModel Model, string ReasoningEffort);
