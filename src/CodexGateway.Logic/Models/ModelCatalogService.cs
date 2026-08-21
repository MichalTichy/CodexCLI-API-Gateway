using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;

namespace CodexGateway.Logic.Models;

public sealed class ModelCatalogService(ICodexControlPlane controlPlane)
{
    public Task<IReadOnlyList<CodexModel>> ListAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false) =>
        controlPlane.GetModelsAsync(forceRefresh, cancellationToken);

    public async Task<ResolvedGenerationModel> ResolveForGenerationAsync(
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
            throw new CodexUnavailableException("Codex authentication is being updated. Try again after device login completes.");
        }

        if (!(await controlPlane.GetAccountAsync(cancellationToken)).Authenticated)
        {
            throw new CodexUnavailableException("The gateway Codex identity is not authenticated.");
        }

        var models = await ListAsync(cancellationToken);
        var model = models.SingleOrDefault(candidate => candidate.Id == modelId)
            ?? throw GatewayException.InvalidRequest($"Model '{modelId}' is not available.", "model_not_found", "model");
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
}
