using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Generation.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.ModelCatalog;

public sealed record ListModelsUseCase(GatewayRequestContext Context) : IRequest<IReadOnlyList<CodexModel>>;

public sealed class ListModelsUseCaseHandler(
    ICodexAuthenticationManager authentication,
    ICodexModelCatalog models)
    : IRequestHandler<ListModelsUseCase, IReadOnlyList<CodexModel>>
{
    public async Task<IReadOnlyList<CodexModel>> Handle(
        ListModelsUseCase request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Context);
        if (string.IsNullOrWhiteSpace(request.Context.ApiKeyId))
        {
            throw new InvalidApiKeyException();
        }

        if (await authentication.GetDeviceLoginAsync(cancellationToken) is { Status: DeviceLoginStatus.Pending })
        {
            throw new CodexUnavailableException(
                "Codex authentication is being updated. Try again after device login completes.");
        }

        if (!(await authentication.GetAccountAsync(cancellationToken)).Authenticated)
        {
            throw new CodexUnavailableException("The gateway Codex identity is not authenticated.");
        }

        return await models.GetModelsAsync(cancellationToken);
    }
}
