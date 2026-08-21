using MediatR;

namespace CodexGateway.Logic.UseCases.ApiKeys;

public sealed record DeleteApiKeyUseCase(string Id) : IRequest;
