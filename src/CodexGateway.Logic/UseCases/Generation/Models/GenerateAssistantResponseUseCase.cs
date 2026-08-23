using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Generation;
using CodexGateway.Logic.Generation.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Generation.Models;

public sealed record GenerateAssistantResponseUseCase(
    AssistantResponseInput Input,
    IGenerationObserver? Observer = null) : IRequest<AssistantResponseResult>;
