using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Tools;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Tools;

public sealed class GetToolCatalogUseCaseHandler(
    IReadOnlyRepository<GatewayState> repository,
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
                var access = await repository.GetBySpecAsync(
                        new ProjectAccessSpecification(
                            request.Context.ProjectId,
                            request.Context.ApiKeyId),
                        token)
                    ?? throw new InvalidApiKeyException();
                var enabled = await repository.GetBySpecAsync(
                        new EnabledMcpServersSpecification(access.Access),
                        token)
                    ?? [];
                var metadata = await discovery.DiscoverAsync(enabled, token);
                return CreateCatalog(enabled, metadata);
            },
            cancellationToken);
    }

    private static ToolCatalog CreateCatalog(
        IReadOnlyList<ResolvedMcpServer> enabledServers,
        IReadOnlyList<DiscoveredMcpServer> discoveredServers)
    {
        var enabledById = enabledServers.ToDictionary(
            server => server.Definition.Id,
            StringComparer.Ordinal);
        var servers = discoveredServers
            .Where(server => server.ServerInfo is not null && enabledById.ContainsKey(server.ServerId))
            .OrderBy(server => server.ServerId, StringComparer.Ordinal)
            .Select(server =>
            {
                var enabled = enabledById[server.ServerId];
                var enabledTools = enabled.EnabledTools.ToHashSet(StringComparer.Ordinal);
                var serverInfo = server.ServerInfo!;
                return new ToolCatalogServer(
                    server.ServerId,
                    enabled.Definition.Name,
                    serverInfo.Version,
                    enabled.Required,
                    serverInfo.Title,
                    serverInfo.Description,
                    serverInfo.WebsiteUrl,
                    serverInfo.Icons,
                    server.Tools
                        .Where(tool => enabledTools.Contains(tool.Name))
                        .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                        .Select(tool => new ToolCatalogTool(
                            server.ServerId,
                            tool.Name,
                            tool.Title,
                            tool.Description,
                            tool.InputSchema,
                            tool.OutputSchema,
                            tool.Annotations,
                            tool.Icons,
                            tool.Meta))
                        .ToArray());
            })
            .ToArray();
        return new ToolCatalog(servers);
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
