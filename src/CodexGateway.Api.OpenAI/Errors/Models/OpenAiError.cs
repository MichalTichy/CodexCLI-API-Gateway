using CodexGateway.Logic.Errors;

namespace CodexGateway.Api.OpenAI.Errors.Models;

public sealed record OpenAiError(
    string Message,
    string Type,
    string? Parameter,
    string Code);
