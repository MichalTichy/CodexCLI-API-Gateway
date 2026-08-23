using MediatR;

namespace CodexGateway.Logic.UseCases.ApiKeys.Models;

public sealed record DeleteApiKeyUseCase(string Id) : IRequest;
