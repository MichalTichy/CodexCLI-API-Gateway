using CodexGateway.Logic.Storage;

namespace CodexGateway.Infrastructure.Storage;

public sealed class ProjectStorage(StoragePaths paths) : IProjectStorage
{
    public void Create(string projectId)
    {
        Directory.CreateDirectory(paths.ProjectArtifacts(projectId));
        Directory.CreateDirectory(paths.ProjectFileMetadata(projectId));
    }

    public void Delete(string projectId)
    {
        var directory = paths.ProjectRoot(projectId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
