using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.ApiKeys.Models;

public sealed record CreateApiKeyUseCase(string Id, string Name, string Key)
    : IRequest<ApiKeyDefinition>;
