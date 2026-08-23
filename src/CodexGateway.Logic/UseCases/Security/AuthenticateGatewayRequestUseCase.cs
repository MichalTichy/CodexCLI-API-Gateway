using CodexGateway.Logic.Specifications;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;
using System.Security.Cryptography;
using System.Text;

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

        var definitions = await repository.GetBySpecAsync(
                new ApiKeyDefinitionsSpecification(),
                cancellationToken)
            ?? [];
        var apiKeyId = FindApiKeyId(definitions, request.ApiKey);
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

    private static string? FindApiKeyId(
        IEnumerable<ApiKeyDefinition> definitions,
        string suppliedKey)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        string? match = null;
        foreach (var definition in definitions)
        {
            var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(definition.Key));
            if (CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash))
            {
                match = definition.Id;
            }
        }

        return match;
    }
}
