using CodexGateway.Logic.Errors;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.ApiKeys;

public sealed record DeleteApiKeyUseCase(string Id) : IRequest;

public sealed class DeleteApiKeyUseCaseHandler(
    IRepository<GatewayState> repository)
    : IRequestHandler<DeleteApiKeyUseCase>
{
    public async Task Handle(DeleteApiKeyUseCase request, CancellationToken cancellationToken)
    {
        await repository.GetAndUpdateAsync(GatewayState.DocumentId, current =>
        {
            if (!current.ApiKeys.Any(key => string.Equals(key.Id, request.Id, StringComparison.Ordinal)))
            {
                throw GatewayException.NotFound($"API key '{request.Id}' was not found.", "api_key_not_found");
            }

            current.ApiKeys = current.ApiKeys
                .Where(key => !string.Equals(key.Id, request.Id, StringComparison.Ordinal))
                .ToList();
            current.Projects = current.Projects.Select(project => project with
            {
                ApiKeyAccess = project.ApiKeyAccess
                    .Where(access => !string.Equals(access.ApiKeyId, request.Id, StringComparison.Ordinal))
                    .ToList()
            }).ToList();
        }, cancellationToken);
    }
}
