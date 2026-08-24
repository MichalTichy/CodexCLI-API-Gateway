using System.Text.Json;
using CodexGateway.Logic.Specifications;
using CodexGateway.Models;

namespace CodexGateway.Logic.Storage;

public interface IFileStore
{
    Task<FileRecord> SaveAsync(
        string? projectId,
        string apiKeyId,
        string fileName,
        string purpose,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileRecord>> ListAsync(
        string? projectId,
        string apiKeyId,
        CancellationToken cancellationToken);

    Task<FileRecord> GetRequiredAsync(
        string? projectId,
        string apiKeyId,
        string fileId,
        CancellationToken cancellationToken);

    Task<(FileRecord Record, Stream Content)> OpenAsync(
        string? projectId,
        string apiKeyId,
        string fileId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string? projectId,
        string apiKeyId,
        string fileId,
        CancellationToken cancellationToken);
}
