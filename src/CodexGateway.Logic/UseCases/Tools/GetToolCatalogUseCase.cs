using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Projects;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Tools;
using MediatR;

namespace CodexGateway.Logic.UseCases.Tools;

public sealed record GetToolCatalogUseCase(GatewayRequestContext Context) : IRequest<ToolCatalog>;
