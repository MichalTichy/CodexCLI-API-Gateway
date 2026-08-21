using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.Storage;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CodexGateway.Tests;

public sealed class FileStoreIsolationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;
    private readonly StoragePaths _paths;
    private readonly FileStore _files;

    public FileStoreIsolationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codex-gateway-file-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var options = Options.Create(new GatewayOptions
        {
            StoragePath = Path.Combine(_root, "data"),
            Files = new CodexGateway.Logic.Configuration.FileOptions { ProjectlessTtlHours = 1 }
        });
        _paths = new StoragePaths(options, new TestHostEnvironment(_root));
        _files = new FileStore(_paths, options);
    }

    [Fact]
    public async Task New_projectless_files_are_stored_and_resolved_by_api_key_id()
    {
        var defaultRecord = await SaveAsync("default", "default.txt");
        var secondaryRecord = await SaveAsync("secondary", "secondary.txt");

        Assert.Equal("default", defaultRecord.ApiKeyId);
        Assert.Equal("secondary", secondaryRecord.ApiKeyId);
        Assert.True(File.Exists(Path.Combine(
            _paths.TemporaryFiles,
            "keys",
            "default",
            defaultRecord.Id,
            "metadata.json")));
        Assert.True(File.Exists(Path.Combine(
            _paths.TemporaryFiles,
            "keys",
            "secondary",
            secondaryRecord.Id,
            "metadata.json")));

        Assert.Equal(defaultRecord.Id, (await _files.ListAsync(null, "default", CancellationToken.None)).Single().Id);
        Assert.Equal(secondaryRecord.Id, (await _files.ListAsync(null, "secondary", CancellationToken.None)).Single().Id);
        await AssertFileNotFoundAsync(() =>
            _files.GetRequiredAsync(null, "secondary", defaultRecord.Id, CancellationToken.None));
        await AssertFileNotFoundAsync(() =>
            _files.GetRequiredAsync(null, "default", secondaryRecord.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cleanup_handles_scoped_projectless_layouts()
    {
        var expiredAt = DateTimeOffset.UtcNow.AddHours(-2);
        var scoped = await CreateRecordAsync("secondary", expiredAt);
        var current = await CreateRecordAsync("default", DateTimeOffset.UtcNow);

        await _files.DeleteExpiredTemporaryFilesAsync(CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(_paths.TemporaryFiles, "keys", "secondary", scoped.Id)));
        Assert.True(Directory.Exists(Path.Combine(_paths.TemporaryFiles, "keys", "default", current.Id)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private async Task<FileRecord> SaveAsync(string apiKeyId, string fileName)
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(fileName));
        return await _files.SaveAsync(
            null,
            apiKeyId,
            fileName,
            "assistants",
            content,
            content.Length,
            CancellationToken.None);
    }

    private async Task<FileRecord> CreateRecordAsync(
        string apiKeyId,
        DateTimeOffset createdAt)
    {
        var id = "file_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_paths.TemporaryFiles, "keys", apiKeyId, id);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "payload"), "test payload");
        var record = new FileRecord
        {
            Id = id,
            FileName = "stored.txt",
            StoredName = "payload",
            Bytes = 12,
            Purpose = "assistants",
            CreatedAt = createdAt,
            ApiKeyId = apiKeyId
        };
        await File.WriteAllTextAsync(
            Path.Combine(directory, "metadata.json"),
            JsonSerializer.Serialize(record, JsonOptions));
        return record;
    }

    private static async Task AssertFileNotFoundAsync(Func<Task<FileRecord>> action)
    {
        var exception = await Assert.ThrowsAsync<GatewayException>(action);
        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("file_not_found", exception.Code);
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CodexGateway.Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
