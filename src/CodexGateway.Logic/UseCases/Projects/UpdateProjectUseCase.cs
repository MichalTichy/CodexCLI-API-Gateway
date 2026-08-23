using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Projects;

public sealed record UpdateProjectUseCase(ProjectDefinition Project) : IRequest<ProjectDefinition>;

public sealed class UpdateProjectUseCaseHandler(
    IRepository<GatewayState> repository,
    RunCoordinator runs)
    : IRequestHandler<UpdateProjectUseCase, ProjectDefinition>
{
    public Task<ProjectDefinition> Handle(
        UpdateProjectUseCase request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Project);
        if (string.IsNullOrWhiteSpace(request.Project.Name))
        {
            throw GatewayException.InvalidRequest("Project name is required.", parameter: "name");
        }

        return runs.ExecuteProjectOperationAsync(
            request.Project.Id,
            token => UpdateAsync(request.Project, token),
            cancellationToken);
    }

    private async Task<ProjectDefinition> UpdateAsync(
        ProjectDefinition replacement,
        CancellationToken cancellationToken)
    {
        ProjectDefinition? updated = null;
        await repository.GetAndUpdateAsync(GatewayState.DocumentId, state =>
        {
            var current = state.Projects.SingleOrDefault(candidate =>
                    string.Equals(candidate.Id, replacement.Id, StringComparison.OrdinalIgnoreCase))
                ?? throw GatewayException.NotFound(
                    $"Project '{replacement.Id}' was not found.",
                    "project_not_found");
            var access = NormalizeApiKeyAccess(
                replacement.ApiKeyAccess,
                state.ApiKeys,
                state.McpServers);
            updated = replacement with
            {
                Id = current.Id,
                Name = replacement.Name.Trim(),
                RunnerImage = NormalizeRunnerImage(replacement.RunnerImage),
                CreatedAt = current.CreatedAt,
                ApiKeyAccess = access
            };
            state.Projects = state.Projects
                .Select(project => string.Equals(project.Id, current.Id, StringComparison.Ordinal)
                    ? updated
                    : project)
                .ToList();
        }, cancellationToken);
        return updated!;
    }

    private static string? NormalizeRunnerImage(string? runnerImage)
    {
        if (string.IsNullOrWhiteSpace(runnerImage))
        {
            return null;
        }

        var normalized = runnerImage.Trim();
        if (normalized.StartsWith("-", StringComparison.Ordinal))
        {
            throw GatewayException.InvalidRequest(
                "Runner image cannot be interpreted as a container-engine option.",
                parameter: "runner_image");
        }

        return normalized;
    }

    private static List<ProjectApiKeyAccess> NormalizeApiKeyAccess(
        IReadOnlyCollection<ProjectApiKeyAccess>? requestedAccess,
        IReadOnlyCollection<ApiKeyDefinition> apiKeys,
        IReadOnlyCollection<McpServerDefinition> catalog)
    {
        var normalized = new List<ProjectApiKeyAccess>();
        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var access in requestedAccess ?? [])
        {
            if (access is null ||
                string.IsNullOrWhiteSpace(access.ApiKeyId) ||
                !apiKeys.Any(key => string.Equals(key.Id, access.ApiKeyId, StringComparison.Ordinal)) ||
                !keyIds.Add(access.ApiKeyId))
            {
                throw GatewayException.InvalidRequest(
                    "Every project API-key access entry must reference one unique configured API key.",
                    parameter: "api_key_access");
            }

            var assignments = new List<ProjectMcpAssignment>();
            var serverIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assignment in access.McpServers ?? [])
            {
                if (assignment is null ||
                    string.IsNullOrWhiteSpace(assignment.ServerId) ||
                    !serverIds.Add(assignment.ServerId))
                {
                    throw GatewayException.InvalidRequest(
                        "Every API-key MCP assignment must reference one unique server.",
                        parameter: "mcp_servers");
                }

                var server = catalog.SingleOrDefault(candidate =>
                    candidate.Enabled &&
                    string.Equals(candidate.Id, assignment.ServerId, StringComparison.Ordinal));
                if (server is null)
                {
                    throw GatewayException.InvalidRequest(
                        $"MCP server '{assignment.ServerId}' is not enabled in the trusted catalog.",
                        parameter: "mcp_servers");
                }

                var enabledTools = (assignment.EnabledTools ?? [])
                    .Where(tool => !string.IsNullOrWhiteSpace(tool))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (enabledTools.Count != (assignment.EnabledTools ?? []).Count ||
                    enabledTools.Any(tool => !server.AvailableTools.Contains(tool, StringComparer.Ordinal)))
                {
                    throw GatewayException.InvalidRequest(
                        $"An enabled tool is not available on MCP server '{server.Id}'.",
                        parameter: "enabled_tools");
                }

                assignments.Add(assignment with
                {
                    ServerId = server.Id,
                    EnabledTools = enabledTools
                });
            }

            normalized.Add(new ProjectApiKeyAccess
            {
                ApiKeyId = access.ApiKeyId,
                McpServers = assignments
                    .OrderBy(assignment => assignment.ServerId, StringComparer.Ordinal)
                    .ToList()
            });
        }

        return normalized
            .OrderBy(access => access.ApiKeyId, StringComparer.Ordinal)
            .ToList();
    }
}
