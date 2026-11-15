using CodexGateway.Logic.Security.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed record EnsureSetupApiKeyUseCase(string Id, string Name) : IRequest<SetupApiKeyResult>;
