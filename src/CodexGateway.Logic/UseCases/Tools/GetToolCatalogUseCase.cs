using CodexGateway.Logic.Tools;
using MediatR;

namespace CodexGateway.Logic.UseCases.Tools;

public sealed record GetToolCatalogUseCase(GatewayRequestContext Context) : IRequest<ToolCatalog>;
