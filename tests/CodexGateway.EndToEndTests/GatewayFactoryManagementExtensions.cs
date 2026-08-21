using CodexGateway.Logic.Codex;
using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests;

internal static class GatewayFactoryManagementExtensions
{
    public static async Task<ProjectDefinition> CreateProjectAsync(
        this GatewayFactory factory,
        string id,
        string name,
        bool enabled = true,
        IEnumerable<ProjectApiKeyAccess>? apiKeyAccess = null,
        CancellationToken cancellationToken = default)
    {
        var sender = factory.Services.GetRequiredService<ISender>();
        await sender.Send(new CreateProjectUseCase(id, name), cancellationToken);
        return await sender.Send(new UpdateProjectUseCase(new ProjectDefinition
        {
            Id = id,
            Name = name,
            Enabled = enabled,
            ApiKeyAccess = apiKeyAccess?.ToList() ??
            [
                new ProjectApiKeyAccess { ApiKeyId = "default" }
            ]
        }), cancellationToken);
    }

    public static Task<ProjectDefinition> UpdateProjectAsync(
        this GatewayFactory factory,
        ProjectDefinition project,
        CancellationToken cancellationToken = default)
    {
        return factory.Services.GetRequiredService<ISender>()
            .Send(new UpdateProjectUseCase(project), cancellationToken);
    }

    public static Task<McpServerDefinition> UpsertMcpServerAsync(
        this GatewayFactory factory,
        McpServerDefinition server,
        CancellationToken cancellationToken = default) =>
        factory.Services.GetRequiredService<ISender>()
            .Send(new CreateOrUpdateMcpServerUseCase(server), cancellationToken);

    public static ICodexControlPlane GetCodexControlPlane(this GatewayFactory factory) =>
        factory.Services.GetRequiredService<ICodexControlPlane>();
}
