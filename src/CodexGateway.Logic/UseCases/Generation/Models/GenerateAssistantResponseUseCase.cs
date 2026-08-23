using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Generation;
using CodexGateway.Logic.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Generation;

public sealed record GenerateAssistantResponseUseCase(
    AssistantResponseInput Input,
    IGenerationObserver? Observer = null) : IRequest<AssistantResponseResult>;
