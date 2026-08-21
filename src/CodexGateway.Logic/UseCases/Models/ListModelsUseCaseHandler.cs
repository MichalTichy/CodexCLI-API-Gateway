using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Models;

public sealed class ListModelsUseCaseHandler(ModelCatalogService models)
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

        return models.ListAsync(cancellationToken, request.ForceRefresh);
    }
}
