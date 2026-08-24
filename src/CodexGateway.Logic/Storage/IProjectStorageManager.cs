namespace CodexGateway.Logic.Storage;

public interface IProjectStorageManager
{
    void Create(string projectId);

    void Delete(string projectId);
}
