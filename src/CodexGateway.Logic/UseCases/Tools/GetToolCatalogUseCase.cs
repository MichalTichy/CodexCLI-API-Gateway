using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Tools;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Tools;

public sealed record GetToolCatalogUseCase(GatewayRequestContext Context) : IRequest<ToolCatalog>;

public sealed class GetToolCatalogUseCaseHandler(
    IReadOnlyRepository<GatewayState> repository,
    IMcpMetadataDiscoveryService discovery) : IRequestHandler<GetToolCatalogUseCase, ToolCatalog>
{
    public async Task<ToolCatalog> Handle(
        GetToolCatalogUseCase request,
        CancellationToken cancellationToken)
    {
        ValidateContext(request.Context);
        if (request.Context.ProjectId is null)
        {
            return new ToolCatalog([CodexBuiltInToolCatalog.Create(WebSearchMode.Disabled)]);
        }

        var access = await repository.GetBySpecAsync(
                new ProjectAccessSpecification(
                    request.Context.ProjectId,
                    request.Context.ApiKeyId),
                cancellationToken)
            ?? throw new InvalidApiKeyException();
        var enabled = await repository.GetBySpecAsync(
                new EnabledMcpServersSpecification(access.Access),
                cancellationToken)
            ?? [];
        var metadata = await discovery.DiscoverAsync(enabled, cancellationToken);
        return CreateCatalog(enabled, metadata, access.Access.WebSearchMode);
    }

    private static ToolCatalog CreateCatalog(
        IReadOnlyList<ResolvedMcpServer> enabledServers,
        IReadOnlyList<DiscoveredMcpServer> discoveredServers,
        WebSearchMode webSearchMode)
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
            .ToList();
        servers.Add(CodexBuiltInToolCatalog.Create(webSearchMode));

        return new ToolCatalog(servers.OrderBy(server => server.Id, StringComparer.Ordinal).ToArray());
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
