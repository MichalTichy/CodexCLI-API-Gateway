using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Projects;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;

namespace CodexGateway.Logic.Files;

public sealed class GatewayFileService(
    IFileStore files,
    ProjectAccessResolver projects,
    RunCoordinator coordinator)
{
    public Task<FileRecord> SaveAsync(
        GatewayRequestContext context,
        string fileName,
        string purpose,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken) =>
        WithinContextAsync(
            context,
            (projectId, token) => files.SaveAsync(
                projectId,
                context.ApiKeyId,
                fileName,
                purpose,
                content,
                declaredLength,
                token),
            cancellationToken);

    public Task<IReadOnlyList<FileRecord>> ListAsync(
        GatewayRequestContext context,
        CancellationToken cancellationToken) =>
        WithinContextAsync(
            context,
            (projectId, token) => files.ListAsync(projectId, context.ApiKeyId, token),
            cancellationToken);

    public Task<FileRecord> GetRequiredAsync(
        GatewayRequestContext context,
        string fileId,
        CancellationToken cancellationToken) =>
        WithinContextAsync(
            context,
            (projectId, token) => files.GetRequiredAsync(projectId, context.ApiKeyId, fileId, token),
            cancellationToken);

    /// <summary>
    /// Reads a file while retaining its project operation gate for the entire
    /// consumer callback. The content stream must not escape the callback.
    /// </summary>
    public Task ReadContentAsync(
        GatewayRequestContext context,
        string fileId,
        Func<FileRecord, Stream, CancellationToken, Task> consume,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consume);
        return WithinContextAsync(
            context,
            async (projectId, token) =>
            {
                var (record, content) = await files.OpenAsync(
                    projectId,
                    context.ApiKeyId,
                    fileId,
                    token);
                await using (content)
                {
                    await consume(record, content, token);
                }

                return true;
            },
            cancellationToken);
    }

    public Task DeleteAsync(
        GatewayRequestContext context,
        string fileId,
        CancellationToken cancellationToken) =>
        WithinContextAsync(
            context,
            async (projectId, token) =>
            {
                await files.DeleteAsync(projectId, context.ApiKeyId, fileId, token);
                return true;
            },
            cancellationToken);

    private async Task<T> WithinContextAsync<T>(
        GatewayRequestContext context,
        Func<string?, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ValidateContext(context);
        if (context.ProjectId is null)
        {
            return await operation(null, cancellationToken);
        }

        return await coordinator.ExecuteProjectOperationAsync(
            context.ProjectId,
            async token =>
            {
                var access = await projects.ResolveAccessAsync(
                    context.ProjectId,
                    context.ApiKeyId,
                    token) ?? throw new InvalidApiKeyException();
                return await operation(access.Project.Id, token);
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
