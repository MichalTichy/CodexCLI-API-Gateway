using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.Setup;
using CodexGateway.Models.Gateway;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.App.Setup;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/setup")
            .AllowAnonymous()
            .AddEndpointFilter<SetupEndpointFilter>();

        group.MapGet("/state", async (IReadOnlyRepository<GatewayState> repository, CancellationToken token) => new
        {
            apiKeys = await repository.GetBySpecAsync(new ApiKeysOrderedByIdSpecification(), token),
            projects = await repository.GetBySpecAsync(new ProjectsOrderedByIdSpecification(), token),
            mcpServers = await repository.GetBySpecAsync(new McpServersOrderedByIdSpecification(), token)
        });
        group.MapPost("/api-keys", (SetupApiKeyRequest request, ISender sender, CancellationToken token) =>
            sender.Send(new EnsureSetupApiKeyUseCase(request.Id, request.Name), token));
        group.MapPut("/projects/{id}", (string id, SetupNameRequest request, ISender sender, CancellationToken token) =>
            sender.Send(new EnsureSetupProjectUseCase(id, request.Name), token));
        group.MapPost("/mcp-servers/discover", (McpServerDefinition request, IMcpMetadataDiscoveryService discovery, CancellationToken token) =>
            discovery.DiscoverAsync([new ResolvedMcpServer(request, [], Required: false)], token));
        group.MapPut("/mcp-servers/{id}", (string id, McpServerDefinition request, bool? replace, ISender sender, CancellationToken token) =>
        {
            if (id != request.Id)
            {
                throw GatewayException.InvalidRequest("The route and server IDs must match.");
            }

            return sender.Send(new RegisterSetupMcpServerUseCase(request, replace ?? false), token);
        });
        group.MapPut("/projects/{projectId}/access/{apiKeyId}", (string projectId, string apiKeyId, ProjectApiKeyAccess request, ISender sender, CancellationToken token) =>
        {
            if (apiKeyId != request.ApiKeyId)
            {
                throw GatewayException.InvalidRequest("The route and API key IDs must match.");
            }

            return sender.Send(new SetSetupProjectAccessUseCase(projectId, request), token);
        });
        group.MapGet("/codex", (ISender sender, CancellationToken token) =>
            sender.Send(new GetCodexAuthenticationStateUseCase(), token));
        group.MapPost("/codex/device-login", (ISender sender, CancellationToken token) =>
            sender.Send(new StartCodexDeviceLoginUseCase(), token));
    }
}
