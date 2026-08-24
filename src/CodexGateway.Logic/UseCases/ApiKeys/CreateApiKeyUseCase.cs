using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CodexGateway.Logic.Errors;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.ApiKeys;

public sealed record CreateApiKeyUseCase(string Id, string Name)
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

        return await repository.GetAndUpdateAsync(GatewayState.DocumentId, current =>
        {
            if (current.ApiKeys.Any(existing =>
                    string.Equals(existing.Id, id, StringComparison.Ordinal)))
            {
                throw GatewayException.InvalidRequest(
                    "An API key with this ID already exists.",
                    "api_key_exists",
                    "id");
            }

            var created = new ApiKeyDefinition
            {
                Id = id,
                Name = name,
                Key = GenerateUniqueKey(current.ApiKeys)
            };
            current.ApiKeys.Add(created);
            return Task.FromResult(created);
        }, cancellationToken);
    }

    private static string GenerateUniqueKey(IReadOnlyCollection<ApiKeyDefinition> existingKeys)
    {
        string key;
        do
        {
            key = "cg_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        }
        while (existingKeys.Any(existing => string.Equals(existing.Key, key, StringComparison.Ordinal)));

        return key;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
