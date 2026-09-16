using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Tools;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;
using System.Text.Json;

namespace CodexGateway.Logic.UseCases.Tools;

public sealed record GetToolCatalogUseCase(GatewayRequestContext Context) : IRequest<ToolCatalog>;

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
                return CreateCatalog(enabled, metadata, access.Access.WebSearchMode);
            },
            cancellationToken);
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
        if (webSearchMode != WebSearchMode.Disabled)
        {
            servers.Add(CreateWebSearchCatalog(webSearchMode));
        }

        return new ToolCatalog(servers.OrderBy(server => server.Id, StringComparer.Ordinal).ToArray());
    }

    private static ToolCatalogServer CreateWebSearchCatalog(WebSearchMode mode)
    {
        var description = mode switch
        {
            WebSearchMode.Cached => "Search the OpenAI-maintained web index without live external retrieval.",
            WebSearchMode.Indexed => "Search indexed public web content with index-gated external retrieval.",
            WebSearchMode.Live => "Search and open current public web content using live retrieval.",
            _ => throw new InvalidOperationException($"Web search mode '{mode}' cannot be advertised.")
        };
        var inputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "The public information to find on the web."
                }
            },
            required = new[] { "query" },
            additionalProperties = false
        });
        var metadata = JsonSerializer.SerializeToElement(new
        {
            source = "codex",
            mode = mode.ToString().ToLowerInvariant()
        });

        return new ToolCatalogServer(
            "codex-built-in",
            "Codex built-in tools",
            "1",
            Required: false,
            "Codex built-in tools",
            "Capabilities provided directly by Codex rather than an MCP server.",
            "https://learn.chatgpt.com/docs/config-file/config-reference",
            Icons: null,
            [new ToolCatalogTool(
                "codex-built-in",
                "web_search",
                "Web search",
                description,
                inputSchema,
                OutputSchema: null,
                Annotations: null,
                Icons: null,
                metadata)]);
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
