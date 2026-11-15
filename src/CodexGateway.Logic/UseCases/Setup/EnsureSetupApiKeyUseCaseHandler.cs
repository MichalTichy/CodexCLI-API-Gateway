using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications.ApiKeys;
using CodexGateway.Logic.UseCases.ApiKeys;
using CodexGateway.Models.Gateway;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed class EnsureSetupApiKeyUseCaseHandler(IReadOnlyRepository<GatewayState> repository, ISender sender)
    : IRequestHandler<EnsureSetupApiKeyUseCase, SetupApiKeyResult>
{
    public async Task<SetupApiKeyResult> Handle(EnsureSetupApiKeyUseCase request, CancellationToken cancellationToken)
    {
        var id = request.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        var keys = await repository.GetBySpecAsync(new ApiKeysOrderedByIdSpecification(), cancellationToken) ?? [];
        var existing = keys.SingleOrDefault(key => key.Id == id);
        if (existing is not null)
        {
            if (existing.Name != request.Name?.Trim())
            {
                throw new GatewayException(409, "setup_collision", "An API key with this ID already exists under a different name. Choose another ID; existing credentials were preserved.");
            }

            return new SetupApiKeyResult(id, false, null);
        }

        var created = await sender.Send(new CreateApiKeyUseCase(id, request.Name!), cancellationToken);
        return new SetupApiKeyResult(created.Id, true, created.Key);
    }
}
