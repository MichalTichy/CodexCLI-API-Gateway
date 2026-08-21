using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Projects;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Tools;
using MediatR;

namespace CodexGateway.Logic.UseCases.Tools;

public sealed class GetToolCatalogUseCaseHandler(
    ProjectAccessResolver projects,
    McpServerResolver mcpServers,
    IMcpMetadataDiscoveryService discovery,
    RunCoordinator coordinator) : IRequestHandler<GetToolCatalogUseCase, ToolCatalog>
{
    public Task<ToolCatalog> Handle(
        GetToolCatalogUseCase request,
        CancellationToken cancellationToken)
    {
        ValidateContext(request.Context);
        if (request.Context.ProjectId is null)
        {
            return Task.FromResult(ToolCatalog.Empty);
        }

        return coordinator.ExecuteAsync(
            request.Context.ProjectId,
            async token =>
            {
                var access = await projects.ResolveAccessAsync(
                    request.Context.ProjectId,
                    request.Context.ApiKeyId,
                    token) ?? throw new InvalidApiKeyException();
                var enabled = await mcpServers.ResolveAsync(access.Access, token);
                var metadata = await discovery.DiscoverAsync(enabled, token);
                return new ToolCatalogProjectionSpecification(enabled).Apply(metadata);
            },
            cancellationToken);
    }

    private static void ValidateContext(GatewayRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.ApiKeyId))
        {
            throw new InvalidApiKeyException();
        }

        if (context.ProjectId is not null && string.IsNullOrWhiteSpace(context.ProjectId))
        {
            throw GatewayException.InvalidRequest("The project ID is invalid.", "invalid_project", "project");
        }
    }
}
