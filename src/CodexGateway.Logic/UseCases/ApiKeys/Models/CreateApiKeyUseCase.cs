using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.ApiKeys;

public sealed record CreateApiKeyUseCase(string Id, string Name, string Key)
    : IRequest<ApiKeyDefinition>;
