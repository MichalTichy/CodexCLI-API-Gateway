using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Generation;
using CodexGateway.Logic.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Generation;

public sealed class GenerateAssistantResponseUseCaseHandler(
    ModelCatalogService models,
    CodexExecutionService execution,
    PromptComposer prompts) : IRequestHandler<GenerateAssistantResponseUseCase, AssistantResponseResult>
{
    public async Task<AssistantResponseResult> Handle(
        GenerateAssistantResponseUseCase request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Input);
        ArgumentNullException.ThrowIfNull(request.Input.Context);

        var input = request.Input;
        var resolved = await models.ResolveForGenerationAsync(
            input.ModelId,
            input.ReasoningEffort,
            cancellationToken);

        var result = await execution.ExecuteAsync(
            input.Context.ProjectId,
            input.Context.ApiKeyId,
            resolved.Model.Id,
            resolved.ReasoningEffort,
            (project, token) => prompts.ComposeAsync(input, project, token),
            request.Observer is null
                ? null
                : token => request.Observer.StartedAsync(
                    new GenerationStarted(resolved.Model.Id, resolved.ReasoningEffort),
                    token).AsTask(),
            request.Observer is null
                ? null
                : (text, token) => request.Observer.TextDeltaAsync(text, token).AsTask(),
            cancellationToken);

        return new AssistantResponseResult(
            resolved.Model.Id,
            resolved.ReasoningEffort,
            result.Text,
            new GenerationUsage(
                result.Usage.InputTokens,
                result.Usage.OutputTokens,
                result.Usage.CachedInputTokens,
                result.Usage.ReasoningOutputTokens));
    }
}
