using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;

namespace CodexGateway.Logic.Projects;

public sealed class ProjectAccessResolver(
    IGatewayConfigurationRepository repository,
    GlobalApiKeyService apiKeys)
{
    public async Task<ProjectDefinition> GetRequiredAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw GatewayException.InvalidRequest("A project ID is required.", parameter: "project_id");
        }

        var project = await repository.QueryAsync(
            new ProjectDefinitionByIdSpecification(projectId),
            cancellationToken);
        if (project is null || !project.Enabled)
        {
            throw GatewayException.NotFound($"Project '{projectId}' was not found.", "project_not_found");
        }

        return project;
    }

    public async Task<ProjectDefinition?> ResolveOptionalAsync(
        string? projectId,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(projectId)
            ? null
            : await GetRequiredAsync(projectId, cancellationToken);

    public async Task<ResolvedProjectAccess?> ResolveAccessAsync(
        string? projectId,
        string? apiKeyId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(apiKeyId) ||
            apiKeys.FindById(apiKeyId) is null)
        {
            return null;
        }

        return await repository.QueryAsync(
            new ProjectAccessSpecification(projectId, apiKeyId),
            cancellationToken);
    }
}
