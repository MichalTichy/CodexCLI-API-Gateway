using CodexGateway.Logic.Specifications;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Security;

public sealed record AuthenticateGatewayRequestUseCase(
    string? ApiKey,
    string? ProjectId) : IRequest<GatewayRequestContext?>;

public sealed class AuthenticateGatewayRequestUseCaseHandler(
    IReadOnlyRepository<GatewayState> repository)
    : IRequestHandler<AuthenticateGatewayRequestUseCase, GatewayRequestContext?>
{
    public async Task<GatewayRequestContext?> Handle(
        AuthenticateGatewayRequestUseCase request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.ApiKey))
        {
            return null;
        }

        var apiKeyId = await repository.GetBySpecAsync(
            new ApiKeyIdBySecretSpecification(request.ApiKey),
            cancellationToken);
        if (apiKeyId is null)
        {
            return null;
        }

        if (request.ProjectId is null)
        {
            return new GatewayRequestContext(apiKeyId, null);
        }

        var access = await repository.GetBySpecAsync(
            new ProjectAccessSpecification(request.ProjectId, apiKeyId),
            cancellationToken);
        return access is null
            ? null
            : new GatewayRequestContext(apiKeyId, access.Project.Id);
    }

}
