using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Generation.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.ModelCatalog.Models;

public sealed record ListModelsUseCase(
    GatewayRequestContext Context,
    bool ForceRefresh = false) : IRequest<IReadOnlyList<CodexModel>>;
