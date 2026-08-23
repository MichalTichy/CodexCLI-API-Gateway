using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Generation;
using CodexGateway.Logic.Models;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Generation;

public sealed class GenerateAssistantResponseUseCaseHandler(
    ICodexControlPlane controlPlane,
    IReadOnlyRepository<GatewayState> repository,
    ICodexRunner runner,
    IWorkspaceManager workspaces,
    RunCoordinator coordinator,
    PromptComposer prompts)
    : IRequestHandler<GenerateAssistantResponseUseCase, AssistantResponseResult>
{
    public async Task<AssistantResponseResult> Handle(
        GenerateAssistantResponseUseCase request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Input);
        ArgumentNullException.ThrowIfNull(request.Input.Context);

        var input = request.Input;
        var resolved = await ResolveModelAsync(input.ModelId, input.ReasoningEffort, cancellationToken);
        var result = await coordinator.ExecuteAsync(
            input.Context.ProjectId,
            token => ExecuteAsync(request, resolved, token),
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

    private async Task<ResolvedGenerationModel> ResolveModelAsync(
        string? modelId,
        string? requestedReasoningEffort,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw GatewayException.InvalidRequest("A model is required.", "model_required", "model");
        }

        if (await controlPlane.GetDeviceLoginAsync(cancellationToken) is { Status: DeviceLoginStatus.Pending })
        {
            throw new CodexUnavailableException(
                "Codex authentication is being updated. Try again after device login completes.");
        }

        if (!(await controlPlane.GetAccountAsync(cancellationToken)).Authenticated)
        {
            throw new CodexUnavailableException("The gateway Codex identity is not authenticated.");
        }

        var models = await controlPlane.GetModelsAsync(false, cancellationToken);
        var model = models.SingleOrDefault(candidate => candidate.Id == modelId)
            ?? throw GatewayException.InvalidRequest(
                $"Model '{modelId}' is not available.",
                "model_not_found",
                "model");
        var reasoningEffort = requestedReasoningEffort ?? model.DefaultReasoningEffort;
        if (!model.SupportedReasoningEfforts.Contains(reasoningEffort, StringComparer.Ordinal))
        {
            throw GatewayException.InvalidRequest(
                $"Reasoning effort '{reasoningEffort}' is not supported by model '{model.Id}'.",
                "unsupported_reasoning_effort",
                "reasoning_effort");
        }

        return new ResolvedGenerationModel(model, reasoningEffort);
    }

    private async Task<CodexRunResult> ExecuteAsync(
        GenerateAssistantResponseUseCase request,
        ResolvedGenerationModel model,
        CancellationToken cancellationToken)
    {
        var input = request.Input;
        var context = input.Context;
        var access = context.ProjectId is null
            ? null
            : await repository.GetBySpecAsync(
                    new ProjectAccessSpecification(context.ProjectId, context.ApiKeyId),
                    cancellationToken)
                ?? throw new InvalidApiKeyException();
        var project = access?.Project;
        var prompt = await prompts.ComposeAsync(input, project, cancellationToken);
        var mcpServers = await repository.GetBySpecAsync(
                new EnabledMcpServersSpecification(access?.Access),
                cancellationToken)
            ?? [];
        var workspace = await workspaces.CreateAsync(
            project,
            context.ApiKeyId,
            prompt.TemporaryFileIds,
            cancellationToken);
        try
        {
            if (prompt.OutputSchema is { } outputSchema)
            {
                await workspaces.StageOutputSchemaAsync(workspace, outputSchema, cancellationToken);
            }

            if (request.Observer is not null)
            {
                await request.Observer.StartedAsync(
                    new GenerationStarted(model.Model.Id, model.ReasoningEffort),
                    cancellationToken);
            }

            var result = await runner.RunAsync(
                new CodexRunRequest(
                    prompt.Text,
                    model.Model.Id,
                    model.ReasoningEffort,
                    workspace,
                    mcpServers,
                    prompt.OutputSchema,
                    project?.RunnerImage),
                request.Observer is null
                    ? null
                    : (text, token) => request.Observer.TextDeltaAsync(text, token).AsTask(),
                cancellationToken);
            await workspaces.CommitAsync(workspace, cancellationToken);
            return result;
        }
        finally
        {
            workspaces.Delete(workspace);
        }
    }
}
