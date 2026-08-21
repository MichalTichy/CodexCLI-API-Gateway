using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.Storage;
using MediatR;

namespace CodexGateway.Logic.UseCases.ApiKeys;

public sealed class DeleteApiKeyUseCaseHandler(
    IGatewayConfigurationRepository repository,
    GlobalApiKeyService apiKeys)
    : IRequestHandler<DeleteApiKeyUseCase>
{
    public async Task Handle(DeleteApiKeyUseCase request, CancellationToken cancellationToken)
    {
        var state = await repository.UpdateAsync(current =>
        {
            if (!current.ApiKeys.Any(key => string.Equals(key.Id, request.Id, StringComparison.Ordinal)))
            {
                throw GatewayException.NotFound($"API key '{request.Id}' was not found.", "api_key_not_found");
            }

            return current with
            {
                ApiKeys = current.ApiKeys
                    .Where(key => !string.Equals(key.Id, request.Id, StringComparison.Ordinal))
                    .ToList(),
                Projects = current.Projects.Select(project => project with
                {
                    ApiKeyAccess = project.ApiKeyAccess
                        .Where(access => !string.Equals(access.ApiKeyId, request.Id, StringComparison.Ordinal))
                        .ToList()
                }).ToList()
            };
        }, cancellationToken);
        apiKeys.Replace(state.ApiKeys);
    }
}
