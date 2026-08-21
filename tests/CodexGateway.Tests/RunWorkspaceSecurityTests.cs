using CodexGateway.Infrastructure.Storage;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CodexGateway.Tests;

public sealed class RunWorkspaceSecurityTests : IDisposable
{
    private readonly string _root;
    private readonly StoragePaths _paths;
    private readonly WorkspaceManager _manager;
    private readonly List<string> _directoryLinks = [];
    private readonly List<string> _fileLinks = [];

    public RunWorkspaceSecurityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codex-gateway-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var options = Options.Create(new GatewayOptions
        {
            StoragePath = Path.Combine(_root, "data"),
            Artifacts = new ArtifactOptions { MaxFiles = 100, MaxFileMegabytes = 1, MaxTotalMegabytes = 2 }
        });
        _paths = new StoragePaths(options, new TestHostEnvironment(_root));
        _manager = new WorkspaceManager(_paths, new FileStore(_paths, options), options);
    }

    [Fact]
    public async Task Create_rejects_a_project_artifact_root_that_is_a_directory_link()
    {
        var project = Project("root-link");
        var outside = Path.Combine(_root, "outside-root");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "outside secret");
        Directory.CreateDirectory(_paths.ProjectRoot(project.Id));
        CreateDirectoryLink(_paths.ProjectArtifacts(project.Id), outside);

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            _manager.CreateAsync(project, "default", [], CancellationToken.None));

        AssertSafeRejection(exception);
        AssertNoPartialRuns();
        Assert.Equal("outside secret", await File.ReadAllTextAsync(Path.Combine(outside, "secret.txt")));
    }

    [Fact]
    public async Task Create_rejects_a_nested_directory_link_without_copying_its_target()
    {
        var project = Project("nested-link");
        var artifacts = _paths.ProjectArtifacts(project.Id);
        var outside = Path.Combine(_root, "outside-nested");
        Directory.CreateDirectory(artifacts);
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "ordinary.txt"), "ordinary");
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "outside secret");
        CreateDirectoryLink(Path.Combine(artifacts, "escape"), outside);

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            _manager.CreateAsync(project, "default", [], CancellationToken.None));

        AssertSafeRejection(exception);
        AssertNoPartialRuns();
        Assert.Equal("outside secret", await File.ReadAllTextAsync(Path.Combine(outside, "secret.txt")));
    }

    [Fact]
    public async Task Output_schema_is_staged_under_gateway_root_and_never_inside_artifacts()
    {
        var workspace = await _manager.CreateAsync(null, "default", [], CancellationToken.None);
        try
        {
            using var schemaDocument = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "answer": { "type": "string" }
                  },
                  "required": ["answer"],
                  "additionalProperties": false
                }
                """);

            await _manager.StageOutputSchemaAsync(
                workspace,
                schemaDocument.RootElement,
                CancellationToken.None);

            var schemaPath = Path.Combine(workspace.RootPath, ".gateway", "output-schema.json");
            Assert.True(File.Exists(schemaPath));
            Assert.False(File.Exists(Path.Combine(workspace.ArtifactsPath, "output-schema.json")));
            using var stagedDocument = JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath));
            Assert.True(JsonElement.DeepEquals(schemaDocument.RootElement, stagedDocument.RootElement));
            await Assert.ThrowsAsync<IOException>(() =>
                _manager.StageOutputSchemaAsync(
                    workspace,
                    schemaDocument.RootElement,
                    CancellationToken.None));
        }
        finally
        {
            _manager.Delete(workspace);
        }
    }

    [Fact]
    public async Task Commit_rejects_a_nested_directory_link_and_preserves_existing_project_artifacts()
    {
        const string projectId = "commit-link";
        var projectArtifacts = _paths.ProjectArtifacts(projectId);
        Directory.CreateDirectory(projectArtifacts);
        await File.WriteAllTextAsync(Path.Combine(projectArtifacts, "original.txt"), "original");

        var outside = Path.Combine(_root, "outside-commit");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "secret.txt");
        await File.WriteAllTextAsync(outsideFile, "outside secret");

        var runRoot = Path.Combine(_paths.Runs, "run_manual");
        var runArtifacts = Path.Combine(runRoot, "artifacts");
        Directory.CreateDirectory(runArtifacts);
        CreateDirectoryLink(Path.Combine(runArtifacts, "escape"), outside);
        var workspace = new RunWorkspace(runRoot, runArtifacts, projectId);

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            _manager.CommitAsync(workspace, CancellationToken.None));

        AssertSafeRejection(exception);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(projectArtifacts, "original.txt")));
        Assert.False(Directory.Exists(Path.Combine(projectArtifacts, "escape")));
        Assert.Equal("outside secret", await File.ReadAllTextAsync(outsideFile));
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(_paths.ProjectRoot(projectId)),
            path => Path.GetFileName(path).StartsWith("artifacts-staging-", StringComparison.Ordinal));

        _manager.Delete(workspace);
        Assert.False(Directory.Exists(runRoot));
        Assert.Equal("outside secret", await File.ReadAllTextAsync(outsideFile));
    }

    [Fact]
    public async Task Commit_rejects_a_file_link_without_reading_its_target()
    {
        var outsideFile = Path.Combine(_root, "outside-file-secret.txt");
        await File.WriteAllTextAsync(outsideFile, "outside secret");
        var runRoot = Path.Combine(_paths.Runs, "run_file_link");
        var runArtifacts = Path.Combine(runRoot, "artifacts");
        Directory.CreateDirectory(runArtifacts);
        var link = Path.Combine(runArtifacts, "leak.txt");
        try
        {
            File.CreateSymbolicLink(link, outsideFile);
            _fileLinks.Add(link);
        }
        catch (Exception linkError) when (OperatingSystem.IsWindows() && linkError is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            _manager.CommitAsync(new RunWorkspace(runRoot, runArtifacts, "file-link"), CancellationToken.None));

        AssertSafeRejection(exception);
        Assert.Equal("outside secret", await File.ReadAllTextAsync(outsideFile));
    }

    [Fact]
    public async Task Commit_rejects_oversized_generated_artifacts()
    {
        var runRoot = Path.Combine(_paths.Runs, "run_oversized");
        var runArtifacts = Path.Combine(runRoot, "artifacts");
        Directory.CreateDirectory(runArtifacts);
        await using (var stream = new FileStream(Path.Combine(runArtifacts, "large.bin"), FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(2L * 1024L * 1024L);
        }

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            _manager.CommitAsync(new RunWorkspace(runRoot, runArtifacts, "oversized"), CancellationToken.None));

        Assert.Equal(413, exception.StatusCode);
        Assert.Equal("artifact_limit_exceeded", exception.Code);
    }

    [Fact]
    public async Task Commit_rejects_a_fifo_without_blocking()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var runRoot = Path.Combine(_paths.Runs, "run_fifo");
        var runArtifacts = Path.Combine(runRoot, "artifacts");
        Directory.CreateDirectory(runArtifacts);
        var fifo = Path.Combine(runArtifacts, "pipe");
        using (var process = Process.Start(new ProcessStartInfo("mkfifo", fifo) { UseShellExecute = false })!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            _manager.CommitAsync(new RunWorkspace(runRoot, runArtifacts, "fifo"), CancellationToken.None));
        AssertSafeRejection(exception);
    }

    public void Dispose()
    {
        foreach (var link in _fileLinks)
        {
            if (File.Exists(link))
            {
                File.Delete(link);
            }
        }

        foreach (var link in _directoryLinks.AsEnumerable().Reverse())
        {
            RemoveDirectoryLink(link);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static ProjectDefinition Project(string id) => new() { Id = id, Name = id };

    private void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            _directoryLinks.Add(link);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not create a test junction.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not create a test junction: " + process.StandardError.ReadToEnd());
        }

        _directoryLinks.Add(link);
    }

    private static void RemoveDirectoryLink(string link)
    {
        if (!Directory.Exists(link) && !File.Exists(link))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Directory.Delete(link);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("rmdir");
        startInfo.ArgumentList.Add(link);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not remove a test junction.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not remove a test junction: " + process.StandardError.ReadToEnd());
        }
    }

    private void AssertNoPartialRuns()
    {
        Assert.Empty(Directory.Exists(_paths.Runs)
            ? Directory.EnumerateFileSystemEntries(_paths.Runs)
            : []);
    }

    private void AssertSafeRejection(GatewayException exception)
    {
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("unsafe_artifact_path", exception.Code);
        Assert.Equal(GatewayErrorCategory.InvalidInput, exception.Category);
        Assert.DoesNotContain(_root, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CodexGateway.Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
