using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Models;

public sealed record ListModelsUseCase(
    GatewayRequestContext Context,
    bool ForceRefresh = false) : IRequest<IReadOnlyList<CodexModel>>;
