using System.Text.Json;
using System.Text.RegularExpressions;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.FileStorage.Files;

public sealed partial class FileStore(StoragePaths paths, IOptions<GatewayOptions> options) : IFileStore
{
    private const string ScopedKeysDirectoryName = "keys";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly GatewayOptions _options = options.Value;

    public async Task<FileRecord> SaveAsync(
        string? projectId,
        string apiKeyId,
        string fileName,
        string purpose,
        Stream content,
        long declaredLength,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!_options.Files.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw GatewayException.InvalidRequest($"Files with extension '{extension}' are not allowed.", "unsupported_file_type", "file");
        }

        var maximumBytes = _options.Files.MaxUploadMegabytes * 1024L * 1024L;
        if (declaredLength > maximumBytes)
        {
            throw new GatewayException(StatusCodes.Status413PayloadTooLarge, "file_too_large", "The uploaded file exceeds the configured size limit.");
        }

        var id = "file_" + Guid.NewGuid().ToString("N");
        var safeName = SanitizeFileName(fileName);
        var createdAt = DateTimeOffset.UtcNow;
        string contentPath;
        string metadataPath;
        string storedName;
        ArtifactUsage? projectArtifactUsage = null;

        if (projectId is null)
        {
            var directory = TemporaryFileDirectory(apiKeyId, id);
            Directory.CreateDirectory(directory);
            storedName = "payload";
            contentPath = Path.Combine(directory, storedName);
            metadataPath = Path.Combine(directory, "metadata.json");
        }
        else
        {
            Directory.CreateDirectory(paths.ProjectArtifacts(projectId));
            Directory.CreateDirectory(paths.ProjectFileMetadata(projectId));
            projectArtifactUsage = ArtifactQuotaGuard.Measure(
                paths.ProjectArtifacts(projectId),
                _options.Artifacts,
                cancellationToken);
            ArtifactQuotaGuard.EnsureAdditionalFileAllowed(
                projectArtifactUsage.Value,
                declaredLength,
                _options.Artifacts);
            storedName = id + "_" + safeName;
            contentPath = Path.Combine(paths.ProjectArtifacts(projectId), storedName);
            metadataPath = Path.Combine(paths.ProjectFileMetadata(projectId), id + ".json");
        }

        long bytes;
        try
        {
            await using var destination = new FileStream(contentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bytes = await CopyWithLimitAsync(
                content,
                destination,
                maximumBytes,
                projectArtifactUsage,
                _options.Artifacts,
                cancellationToken);
        }
        catch
        {
            if (File.Exists(contentPath))
            {
                File.Delete(contentPath);
            }

            if (projectId is null)
            {
                var temporaryDirectory = Path.GetDirectoryName(contentPath)!;
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }

            throw;
        }

        var record = new FileRecord
        {
            Id = id,
            FileName = safeName,
            StoredName = storedName,
            Bytes = bytes,
            Purpose = string.IsNullOrWhiteSpace(purpose) ? "assistants" : purpose,
            CreatedAt = createdAt,
            ProjectId = projectId,
            ApiKeyId = projectId is null ? apiKeyId : null
        };
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(record, JsonOptions), cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<FileRecord>> ListAsync(
        string? projectId,
        string apiKeyId,
        CancellationToken cancellationToken)
    {
        var metadataFiles = projectId is null
            ? EnumerateProjectlessMetadata(apiKeyId)
            : Directory.Exists(paths.ProjectFileMetadata(projectId))
                ? Directory.EnumerateFiles(paths.ProjectFileMetadata(projectId), "*.json", SearchOption.TopDirectoryOnly)
                    .Select(path => new MetadataCandidate(
                        path,
                        Path.GetFileNameWithoutExtension(path)))
                : [];

        var records = new List<FileRecord>();
        foreach (var candidate in metadataFiles)
        {
            var record = await ReadMetadataAsync(candidate.Path, cancellationToken);
            if (projectId is null && record is not null && record.ProjectId is null && IsExpired(record))
            {
                DeleteTemporaryDirectory(Path.GetDirectoryName(candidate.Path)!);
                continue;
            }

            if (record is not null && RecordMatchesScope(record, projectId, apiKeyId, candidate))
            {
                var refreshed = TryRefreshContent(record);
                if (refreshed is not null)
                {
                    records.Add(refreshed);
                }
            }
        }

        return records.OrderByDescending(record => record.CreatedAt).ToArray();
    }

    public async Task<FileRecord> GetRequiredAsync(
        string? projectId,
        string apiKeyId,
        string fileId,
        CancellationToken cancellationToken)
    {
        if (!FileIdPattern().IsMatch(fileId))
        {
            throw GatewayException.NotFound($"File '{fileId}' was not found.", "file_not_found");
        }

        foreach (var candidate in MetadataCandidates(projectId, apiKeyId, fileId))
        {
            var record = await ReadMetadataAsync(candidate.Path, cancellationToken);
            if (record is null || !RecordMatchesScope(record, projectId, apiKeyId, candidate))
            {
                continue;
            }

            if (record.ProjectId is null && IsExpired(record))
            {
                DeleteTemporaryDirectory(Path.GetDirectoryName(candidate.Path)!);
                continue;
            }

            var refreshed = TryRefreshContent(record);
            if (refreshed is not null)
            {
                return refreshed;
            }
        }

        throw GatewayException.NotFound($"File '{fileId}' was not found.", "file_not_found");
    }

    public async Task<(FileRecord Record, Stream Content)> OpenAsync(
        string? projectId,
        string apiKeyId,
        string fileId,
        CancellationToken cancellationToken)
    {
        var record = await GetRequiredAsync(projectId, apiKeyId, fileId, cancellationToken);
        var contentPath = ContentPath(record);
        if (!File.Exists(contentPath))
        {
            throw GatewayException.NotFound($"File '{fileId}' was not found.", "file_not_found");
        }

        return (record, File.OpenRead(contentPath));
    }

    public async Task DeleteAsync(
        string? projectId,
        string apiKeyId,
        string fileId,
        CancellationToken cancellationToken)
    {
        var record = await GetRequiredAsync(projectId, apiKeyId, fileId, cancellationToken);
        var metadataPath = MetadataPath(record);
        var contentPath = ContentPath(record);
        if (File.Exists(contentPath))
        {
            File.Delete(contentPath);
        }

        if (projectId is null)
        {
            var directory = Path.GetDirectoryName(metadataPath)!;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        else if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
    }

    public string ContentPath(FileRecord record) => record.ProjectId is null
        ? Path.Combine(TemporaryFileDirectory(record), record.StoredName)
        : Path.Combine(paths.ProjectArtifacts(record.ProjectId), record.StoredName);

    internal string TemporaryFileDirectory(FileRecord record)
    {
        if (record.ProjectId is not null)
        {
            throw new InvalidOperationException("Only projectless files have a temporary-file directory.");
        }

        return TemporaryFileDirectory(
            record.ApiKeyId ?? throw new InvalidOperationException(
                "Projectless file metadata must contain an API key identity."),
            record.Id);
    }

    public async Task DeleteExpiredTemporaryFilesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.TemporaryFiles))
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddHours(-_options.Files.ProjectlessTtlHours);
        foreach (var directory in EnumerateTemporaryFileDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(directory, "metadata.json");
            var record = await ReadMetadataAsync(metadataPath, cancellationToken);
            if (record is null || record.CreatedAt < cutoff)
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        DeleteEmptyKeyDirectories();
    }

    private string MetadataPath(FileRecord record) => record.ProjectId is null
        ? Path.Combine(TemporaryFileDirectory(record), "metadata.json")
        : Path.Combine(paths.ProjectFileMetadata(record.ProjectId), record.Id + ".json");

    private IEnumerable<MetadataCandidate> EnumerateProjectlessMetadata(string apiKeyId)
    {
        var scopedDirectory = ScopedKeyDirectory(apiKeyId);
        if (Directory.Exists(scopedDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(scopedDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                var fileId = Path.GetFileName(directory);
                if (FileIdPattern().IsMatch(fileId))
                {
                    yield return new MetadataCandidate(Path.Combine(directory, "metadata.json"), fileId);
                }
            }
        }
    }

    private IEnumerable<MetadataCandidate> MetadataCandidates(string? projectId, string apiKeyId, string fileId)
    {
        if (projectId is not null)
        {
            yield return new MetadataCandidate(
                Path.Combine(paths.ProjectFileMetadata(projectId), fileId + ".json"),
                fileId);
            yield break;
        }

        yield return new MetadataCandidate(
            Path.Combine(TemporaryFileDirectory(apiKeyId, fileId), "metadata.json"),
            fileId);
    }

    private static bool RecordMatchesScope(
        FileRecord record,
        string? projectId,
        string apiKeyId,
        MetadataCandidate candidate)
    {
        if (!string.Equals(record.Id, candidate.FileId, StringComparison.Ordinal))
        {
            return false;
        }

        if (projectId is not null)
        {
            return string.Equals(record.ProjectId, projectId, StringComparison.Ordinal) && record.ApiKeyId is null;
        }

        if (record.ProjectId is not null)
        {
            return false;
        }

        return string.Equals(record.ApiKeyId, apiKeyId, StringComparison.Ordinal);
    }

    private string TemporaryFileDirectory(string apiKeyId, string fileId) =>
        Path.Combine(ScopedKeyDirectory(apiKeyId), fileId);

    private string ScopedKeyDirectory(string apiKeyId)
    {
        if (!ApiKeyIdPattern().IsMatch(apiKeyId))
        {
            throw new InvalidOperationException("The authenticated API key identity is invalid.");
        }

        return Path.Combine(paths.TemporaryFiles, ScopedKeysDirectoryName, apiKeyId);
    }

    private IEnumerable<string> EnumerateTemporaryFileDirectories()
    {
        var keysDirectory = Path.Combine(paths.TemporaryFiles, ScopedKeysDirectoryName);
        if (!Directory.Exists(keysDirectory))
        {
            yield break;
        }

        foreach (var keyDirectory in Directory.EnumerateDirectories(keysDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            foreach (var fileDirectory in Directory.EnumerateDirectories(keyDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                yield return fileDirectory;
            }
        }
    }

    private void DeleteEmptyKeyDirectories()
    {
        var keysDirectory = Path.Combine(paths.TemporaryFiles, ScopedKeysDirectoryName);
        if (!Directory.Exists(keysDirectory))
        {
            return;
        }

        foreach (var keyDirectory in Directory.EnumerateDirectories(keysDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(keyDirectory).Any())
                {
                    Directory.Delete(keyDirectory, false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A concurrent upload may be creating a file. A later cleanup pass can retry.
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(keysDirectory).Any())
            {
                Directory.Delete(keysDirectory, false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A concurrent upload may be creating a key directory. A later cleanup pass can retry.
        }
    }

    private static async Task<FileRecord?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<FileRecord>(stream, JsonOptions, cancellationToken);
    }

    private bool IsExpired(FileRecord record) =>
        record.CreatedAt < DateTimeOffset.UtcNow.AddHours(-_options.Files.ProjectlessTtlHours);

    private FileRecord? TryRefreshContent(FileRecord record)
    {
        var path = ContentPath(record);
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return null;
            }

            return record with { Bytes = new FileInfo(path).Length };
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
            // A concurrent request may still have the file open. It remains hidden and cleanup will retry later.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best effort; expired records are never returned even if deletion must be retried.
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = string.Concat(name.Select(character => char.IsControl(character) || character is '"' or '\'' ? '_' : character));

        return string.IsNullOrWhiteSpace(name) ? "upload.txt" : name;
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        ArtifactUsage? existingArtifactUsage,
        ArtifactOptions artifactLimits,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return total;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new GatewayException(StatusCodes.Status413PayloadTooLarge, "file_too_large", "The uploaded file exceeds the configured size limit.");
            }

            if (existingArtifactUsage is { } usage)
            {
                ArtifactQuotaGuard.EnsureAdditionalFileAllowed(usage, total, artifactLimits);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    [GeneratedRegex("^file_[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex FileIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyIdPattern();

    private sealed record MetadataCandidate(string Path, string FileId);
}
