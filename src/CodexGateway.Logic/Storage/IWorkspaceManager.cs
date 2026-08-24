using System.Text.Json;
using CodexGateway.Logic.Specifications;
using CodexGateway.Models;

namespace CodexGateway.Logic.Storage;

public interface IWorkspaceManager
{
    Task<RunWorkspace> CreateEmptyAsync(CancellationToken cancellationToken);

    Task<RunWorkspace> CreateAsync(
        ProjectDefinition? project,
        string apiKeyId,
        IReadOnlyCollection<string> temporaryFileIds,
        CancellationToken cancellationToken);

    Task StageOutputSchemaAsync(
        RunWorkspace workspace,
        JsonElement outputSchema,
        CancellationToken cancellationToken);

    Task CommitAsync(RunWorkspace workspace, CancellationToken cancellationToken);

    void Delete(RunWorkspace workspace);
}
