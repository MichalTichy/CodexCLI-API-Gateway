using CodexGateway.Logic.Errors;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;
using System.Text.RegularExpressions;

namespace CodexGateway.Logic.UseCases.ApiKeys;

public sealed record CreateApiKeyUseCase(string Id, string Name, string Key)
    : IRequest<ApiKeyDefinition>;

public sealed partial class CreateApiKeyUseCaseHandler(
    IRepository<GatewayState> repository)
    : IRequestHandler<CreateApiKeyUseCase, ApiKeyDefinition>
{
    public async Task<ApiKeyDefinition> Handle(
        CreateApiKeyUseCase request,
        CancellationToken cancellationToken)
    {
        var id = request.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var key = request.Key?.Trim() ?? string.Empty;
        if (!IdPattern().IsMatch(id))
        {
            throw GatewayException.InvalidRequest(
                "API key IDs must contain 1-64 lowercase letters, numbers, underscores, or hyphens.",
                parameter: "id");
        }

        if (name.Length is 0 or > 128)
        {
            throw GatewayException.InvalidRequest("API key name is required and limited to 128 characters.", parameter: "name");
        }

        if (key.Length == 0 || !string.Equals(key, request.Key, StringComparison.Ordinal))
        {
            throw GatewayException.InvalidRequest("API key is required and cannot start or end with whitespace.", parameter: "key");
        }

        var created = new ApiKeyDefinition { Id = id, Name = name, Key = key };
        await repository.GetAndUpdateAsync(GatewayState.DocumentId, current =>
        {
            if (current.ApiKeys.Any(existing =>
                    string.Equals(existing.Id, id, StringComparison.Ordinal) ||
                    string.Equals(existing.Key, key, StringComparison.Ordinal)))
            {
                throw GatewayException.InvalidRequest(
                    "API key IDs and secrets must be unique.",
                    "api_key_exists",
                    "id");
            }

            current.ApiKeys.Add(created);
        }, cancellationToken);
        return created;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
