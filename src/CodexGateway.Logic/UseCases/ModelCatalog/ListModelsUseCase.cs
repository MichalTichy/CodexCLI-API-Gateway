using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Generation.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.ModelCatalog;

public sealed record ListModelsUseCase(
    GatewayRequestContext Context,
    bool ForceRefresh = false) : IRequest<IReadOnlyList<CodexModel>>;

public sealed class ListModelsUseCaseHandler(ICodexControlPlane codex)
    : IRequestHandler<ListModelsUseCase, IReadOnlyList<CodexModel>>
{
    public Task<IReadOnlyList<CodexModel>> Handle(
        ListModelsUseCase request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Context);
        if (string.IsNullOrWhiteSpace(request.Context.ApiKeyId))
        {
            throw new InvalidApiKeyException();
        }

        return codex.GetModelsAsync(request.ForceRefresh, cancellationToken);
    }
}
