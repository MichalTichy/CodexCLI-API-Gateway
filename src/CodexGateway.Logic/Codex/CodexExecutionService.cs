using CodexGateway.Logic.Errors;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Projects;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;

namespace CodexGateway.Logic.Codex;

public sealed class CodexExecutionService(
    ICodexRunner runner,
    ProjectAccessResolver projects,
    McpServerResolver mcpServers,
    IWorkspaceManager workspaces,
    RunCoordinator coordinator)
{
    public Task<CodexRunResult> ExecuteAsync(
        string? requestedProjectId,
        string apiKeyId,
        string model,
        string reasoningEffort,
        Func<ProjectDefinition?, CancellationToken, Task<CodexPrompt>> preparePrompt,
        Func<CancellationToken, Task>? onStarted,
        Func<string, CancellationToken, Task>? onText,
        CancellationToken cancellationToken) =>
        coordinator.ExecuteAsync(requestedProjectId, async runCancellation =>
        {
            var resolvedAccess = requestedProjectId is null
                ? null
                : await projects.ResolveAccessAsync(requestedProjectId, apiKeyId, runCancellation)
                  ?? throw new InvalidApiKeyException();
            var currentProject = resolvedAccess?.Project;
            var currentProjectAccess = resolvedAccess?.Access;
            var prompt = await preparePrompt(currentProject, runCancellation);
            var resolvedMcpServers = await mcpServers.ResolveAsync(currentProjectAccess, runCancellation);
            var workspace = await workspaces.CreateAsync(currentProject, apiKeyId, prompt.TemporaryFileIds, runCancellation);
            try
            {
                if (prompt.OutputSchema is { } outputSchema)
                {
                    await workspaces.StageOutputSchemaAsync(workspace, outputSchema, runCancellation);
                }

                if (onStarted is not null)
                {
                    await onStarted(runCancellation);
                }

                var outcome = await runner.RunAsync(
                    new CodexRunRequest(
                        prompt.Text,
                        model,
                        reasoningEffort,
                        workspace,
                        resolvedMcpServers,
                        prompt.OutputSchema,
                        currentProject?.RunnerImage),
                    onText,
                    runCancellation);
                await workspaces.CommitAsync(workspace, runCancellation);
                return outcome;
            }
            finally
            {
                workspaces.Delete(workspace);
            }
        }, cancellationToken);
}
