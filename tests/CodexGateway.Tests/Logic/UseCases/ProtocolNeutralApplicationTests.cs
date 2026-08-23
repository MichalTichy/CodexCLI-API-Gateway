using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.Persistence;
using CodexGateway.Logic;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Generation;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Logic.Tools;
using CodexGateway.Logic.UseCases.Files;
using CodexGateway.Logic.UseCases.Generation;
using CodexGateway.Logic.UseCases.ModelCatalog.Models;
using CodexGateway.Logic.UseCases.Security;
using CodexGateway.Logic.UseCases.Tools;
using CodexGateway.Models;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Tests.Logic.UseCases;

public sealed class ProtocolNeutralApplicationTests
{
    [Fact]
    public async Task Prompt_composer_maps_semantic_content_files_and_structured_output()
    {
        var files = new StubFileStore
        {
            Record = File("file-one", "notes.txt", "stored-notes.txt")
        };
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var input = new AssistantResponseInput(
            new GatewayRequestContext("default", null),
            "gpt-test",
            "high",
            [
                new InputMessage(
                    GenerationMessageRole.System,
                    [new TextContentPart("Be concise.")]),
                new InputMessage(
                    GenerationMessageRole.User,
                    [new TextContentPart("Summarize the file."), new FileContentPart("file-one")])
            ],
            ["file-one"],
            new StructuredOutput("summary", "A short summary", schema));

        var prompt = await new PromptComposer(files).ComposeAsync(input, null, CancellationToken.None);

        Assert.Contains("[system]\nBe concise.", NormalizeNewlines(prompt.Text), StringComparison.Ordinal);
        Assert.Contains("[user]\nSummarize the file.\n[Attached file: file-one]", NormalizeNewlines(prompt.Text), StringComparison.Ordinal);
        Assert.Contains("./artifacts/file-one_notes.txt", prompt.Text, StringComparison.Ordinal);
        Assert.Contains("structured output 'summary'", prompt.Text, StringComparison.Ordinal);
        Assert.Equal(["file-one"], prompt.TemporaryFileIds);
        Assert.Equal("object", prompt.OutputSchema?.GetProperty("type").GetString());
        Assert.Equal(1, files.GetCount);
    }

    [Fact]
    public async Task List_models_use_case_returns_the_control_plane_catalog()
    {
        var controlPlane = new StubControlPlane
        {
            Models = [new CodexModel("gpt-test", "Test", ["low", "high"], "high")]
        };
        var listed = await new ListModelsUseCaseHandler(controlPlane).Handle(
            new ListModelsUseCase(new GatewayRequestContext("default", null)),
            CancellationToken.None);

        Assert.Equal("gpt-test", Assert.Single(listed).Id);
    }

    [Fact]
    public async Task Authentication_use_case_is_the_single_context_factory()
    {
        var project = new ProjectDefinition
        {
            Id = "project-one",
            Name = "Project One",
            ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "default" }]
        };
        var repository = new InMemoryGatewayStateRepository(new GatewayState
        {
            ApiKeys = [new ApiKeyDefinition { Id = "default", Name = "Default", Key = "test-secret" }],
            Projects = [project]
        });
        var access = new AuthenticateGatewayRequestUseCaseHandler(repository);

        Assert.Null(await access.Handle(
            new AuthenticateGatewayRequestUseCase("wrong-secret", null),
            CancellationToken.None));
        var projectless = await access.Handle(
            new AuthenticateGatewayRequestUseCase("test-secret", null),
            CancellationToken.None);
        var projectContext = await access.Handle(
            new AuthenticateGatewayRequestUseCase("test-secret", project.Id),
            CancellationToken.None);

        Assert.Equal(new GatewayRequestContext("default", null), projectless);
        Assert.Equal(new GatewayRequestContext("default", project.Id), projectContext);
        Assert.Null(await access.Handle(
            new AuthenticateGatewayRequestUseCase("test-secret", "missing-project"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Project_access_specification_requires_one_enabled_matching_grant()
    {
        var enabled = new ProjectDefinition
        {
            Id = "project-one",
            Name = "Project One",
            ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "default" }]
        };
        ISpecification<GatewayState, ResolvedProjectAccess?> specification =
            new ProjectAccessSpecification("PROJECT-ONE", "default");

        var resolved = await specification.ApplyAsync(
            new[] { new GatewayState { Projects = [enabled] } }.AsQueryable());

        Assert.Equal(enabled, resolved?.Project);
        Assert.Null(await specification.ApplyAsync(new[]
        {
            new GatewayState { Projects = [enabled with { Enabled = false }] }
        }.AsQueryable()));
        Assert.Null(await specification.ApplyAsync(
            new[] { new GatewayState
            {
                Projects =
                [
                    enabled with
                    {
                        ApiKeyAccess =
                        [
                            new ProjectApiKeyAccess { ApiKeyId = "default" },
                            new ProjectApiKeyAccess { ApiKeyId = "default" }
                        ]
                    }
                ]
            }}.AsQueryable()));
    }

    [Fact]
    public async Task Generate_assistant_response_use_case_runs_codex_and_exposes_transport_neutral_events()
    {
        var state = new InMemoryGatewayStateRepository(new GatewayState());
        var options = TestOptions();
        var workspaces = new StubWorkspaceManager();
        var runner = new StubCodexRunner();
        var handler = new GenerateAssistantResponseUseCaseHandler(
            new StubControlPlane
            {
                Models = [new CodexModel("gpt-test", "Test", ["medium"], "medium")]
            },
            state,
            runner,
            workspaces,
            new RunCoordinator(options),
            new PromptComposer(new StubFileStore()));
        var observer = new RecordingGenerationObserver();

        var result = await handler.Handle(
            new GenerateAssistantResponseUseCase(
                new AssistantResponseInput(
                    new GatewayRequestContext("default", null),
                    "gpt-test",
                    null,
                    [new InputMessage(GenerationMessageRole.User, [new TextContentPart("Hello")])],
                    []),
                observer),
            CancellationToken.None);

        Assert.Equal("answer", result.Text);
        Assert.Equal("medium", result.ReasoningEffort);
        Assert.Equal(15, result.Usage.TotalTokens);
        Assert.Equal(["started:gpt-test:medium", "delta:partial"], observer.Events);
        Assert.Contains("[user]", runner.LastRequest?.Prompt, StringComparison.Ordinal);
        Assert.True(workspaces.Committed);
        Assert.True(workspaces.Deleted);
    }

    [Fact]
    public async Task File_content_callback_holds_project_gate_until_consumption_finishes()
    {
        var project = new ProjectDefinition
        {
            Id = "project-one",
            Name = "Project One",
            ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "default" }]
        };
        var state = new InMemoryGatewayStateRepository(new GatewayState { Projects = [project] });
        var options = TestOptions();
        var files = new StubFileStore
        {
            Record = File("file-one", "notes.txt", "stored-notes.txt"),
            Content = new TrackingMemoryStream(Encoding.UTF8.GetBytes("content"))
        };
        var coordinator = new RunCoordinator(options);
        var readHandler = new ReadFileUseCaseHandler(files, state, coordinator);
        var deleteHandler = new DeleteFileUseCaseHandler(files, state, coordinator);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new GatewayRequestContext("default", project.Id);

        var read = readHandler.Handle(
            new ReadFileUseCase(
                context,
                "file-one",
                async (_, content, cancellationToken) =>
                {
                    callbackEntered.SetResult();
                    await releaseCallback.Task.WaitAsync(cancellationToken);
                    Assert.Equal("content", await new StreamReader(content).ReadToEndAsync(cancellationToken));
                }),
            CancellationToken.None);
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var delete = deleteHandler.Handle(
            new DeleteFileUseCase(context, "file-one"),
            CancellationToken.None);
        var prematureDelete = await Task.WhenAny(files.DeleteEntered.Task, Task.Delay(100));
        Assert.NotSame(files.DeleteEntered.Task, prematureDelete);

        releaseCallback.SetResult();
        await Task.WhenAll(read, delete);

        Assert.True(files.Content.Disposed);
        Assert.True(files.DeleteEntered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task File_upload_holds_project_gate_while_the_request_stream_is_consumed()
    {
        var project = new ProjectDefinition
        {
            Id = "project-one",
            Name = "Project One",
            ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "default" }]
        };
        var state = new InMemoryGatewayStateRepository(new GatewayState { Projects = [project] });
        var options = TestOptions();
        var files = new StubFileStore { BlockSave = true };
        var coordinator = new RunCoordinator(options);
        var saveHandler = new SaveFileUseCaseHandler(files, state, coordinator);
        var deleteHandler = new DeleteFileUseCaseHandler(files, state, coordinator);
        var context = new GatewayRequestContext("default", project.Id);

        var save = saveHandler.Handle(
            new SaveFileUseCase(
                context,
                "notes.txt",
                "assistants",
                new MemoryStream([1, 2, 3]),
                3),
            CancellationToken.None);
        await files.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var delete = deleteHandler.Handle(
            new DeleteFileUseCase(context, "file-one"),
            CancellationToken.None);
        var prematureDelete = await Task.WhenAny(files.DeleteEntered.Task, Task.Delay(100));
        Assert.NotSame(files.DeleteEntered.Task, prematureDelete);

        files.ReleaseSave.SetResult();
        await Task.WhenAll(save, delete);
        Assert.True(files.DeleteEntered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Get_file_use_case_revalidates_a_project_grant_at_operation_time()
    {
        var project = new ProjectDefinition
        {
            Id = "project-one",
            Name = "Project One",
            ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "default" }]
        };
        var state = new InMemoryGatewayStateRepository(new GatewayState { Projects = [project] });
        var options = TestOptions();
        var files = new StubFileStore();
        var handler = new GetFileUseCaseHandler(files, state, new RunCoordinator(options));
        var previouslyAuthenticatedContext = new GatewayRequestContext("default", project.Id);
        await state.GetAndUpdateAsync(GatewayState.DocumentId, current =>
        {
            current.Projects =
            [
                project with
                {
                    ApiKeyAccess = []
                }
            ];
            return Task.FromResult(true);
        });

        var exception = await Assert.ThrowsAsync<InvalidApiKeyException>(() =>
            handler.Handle(
                new GetFileUseCase(previouslyAuthenticatedContext, "file-one"),
                CancellationToken.None));

        Assert.Equal(GatewayErrorCategory.Unauthenticated, exception.Category);
        Assert.Equal(0, files.GetCount);
    }

    [Fact]
    public async Task Projectless_file_operations_remain_private_to_the_api_key_without_project_locking()
    {
        var options = TestOptions();
        var state = new InMemoryGatewayStateRepository(new GatewayState());
        var files = new StubFileStore();
        var handler = new SaveFileUseCaseHandler(files, state, new RunCoordinator(options));

        await handler.Handle(
            new SaveFileUseCase(
                new GatewayRequestContext("secondary-key", null),
                "notes.txt",
                "assistants",
                new MemoryStream([1, 2, 3]),
                3),
            CancellationToken.None);

        Assert.Null(files.LastProjectId);
        Assert.Equal("secondary-key", files.LastApiKeyId);
    }

    [Fact]
    public async Task Tool_catalog_is_typed_and_distinguishes_visible_from_invocable_tools()
    {
        var server = new HttpMcpServerDefinition
        {
            Id = "catalog",
            Name = "Catalog",
            Enabled = true,
            Url = "https://mcp.example.test",
            AvailableTools = ["read", "write", "hidden"]
        };
        var project = new ProjectDefinition
        {
            Id = "project-one",
            Name = "Project One",
            ApiKeyAccess =
            [
                new ProjectApiKeyAccess
                {
                    ApiKeyId = "default",
                    McpServers =
                    [
                        new ProjectMcpAssignment
                        {
                            ServerId = server.Id,
                            Required = true,
                            EnabledTools = ["read", "write"]
                        }
                    ]
                }
            ]
        };
        var state = new InMemoryGatewayStateRepository(new GatewayState
        {
            Projects = [project],
            McpServers = [server]
        });
        var options = TestOptions();
        var handler = new GetToolCatalogUseCaseHandler(
            state,
            new StubMcpDiscovery(),
            new RunCoordinator(options));

        var result = await handler.Handle(
            new GetToolCatalogUseCase(new GatewayRequestContext("default", project.Id)),
            CancellationToken.None);

        var catalogServer = Assert.Single(result.Servers);
        Assert.True(catalogServer.Required);
        Assert.Equal(["read", "write"], catalogServer.Tools.Select(tool => tool.Name));
        Assert.Equal("catalog/write", catalogServer.Tools.Single(tool => tool.Name == "write").Id);
    }

    private static IOptions<GatewayOptions> TestOptions() => Options.Create(new GatewayOptions
    {
        Limits = new RunLimitOptions
        {
            MaxConcurrent = 2,
            MaxQueued = 2,
            TimeoutSeconds = 30
        }
    });

    private static FileRecord File(string id, string fileName, string storedName) => new()
    {
        Id = id,
        FileName = fileName,
        StoredName = storedName,
        Bytes = 7,
        Purpose = "assistants",
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class StubControlPlane : ICodexControlPlane
    {
        public IReadOnlyList<CodexModel> Models { get; init; } = [];

        public CodexAccountStatus Account { get; init; } = new(true, "chatgpt", null);

        public DeviceLogin? Login { get; init; }

        public Task<IReadOnlyList<CodexModel>> GetModelsAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(Models);

        public Task<CodexAccountStatus> GetAccountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Account);

        public Task<DeviceLogin> StartDeviceLoginAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeviceLogin?> GetDeviceLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Login);

        public Task CancelDeviceLoginAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubCodexRunner : ICodexRunner
    {
        public CodexRunRequest? LastRequest { get; private set; }

        public async Task<CodexRunResult> RunAsync(
            CodexRunRequest request,
            Func<string, CancellationToken, Task>? onText,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (onText is not null)
            {
                await onText("partial", cancellationToken);
            }

            return new CodexRunResult("answer", new CodexUsage(10, 5, 2, 1));
        }
    }

    private sealed class RecordingGenerationObserver : IGenerationObserver
    {
        public List<string> Events { get; } = [];

        public ValueTask StartedAsync(GenerationStarted started, CancellationToken cancellationToken)
        {
            Events.Add($"started:{started.ModelId}:{started.ReasoningEffort}");
            return ValueTask.CompletedTask;
        }

        public ValueTask TextDeltaAsync(string text, CancellationToken cancellationToken)
        {
            Events.Add("delta:" + text);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
        public bool Committed { get; private set; }

        public bool Deleted { get; private set; }

        public Task<RunWorkspace> CreateEmptyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RunWorkspace("root", "artifacts", null));

        public Task<RunWorkspace> CreateAsync(
            ProjectDefinition? project,
            string apiKeyId,
            IReadOnlyCollection<string> temporaryFileIds,
            CancellationToken cancellationToken) =>
            CreateEmptyAsync(cancellationToken);

        public Task StageOutputSchemaAsync(
            RunWorkspace workspace,
            JsonElement outputSchema,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CommitAsync(RunWorkspace workspace, CancellationToken cancellationToken)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public void Delete(RunWorkspace workspace) => Deleted = true;
    }

    private sealed class StubProjectStorage : IProjectStorage
    {
        public void Create(string projectId)
        {
        }

        public void Delete(string projectId)
        {
        }
    }

    private sealed class StubFileStore : IFileStore
    {
        public FileRecord Record { get; init; } = File("unused", "unused.txt", "unused.txt");

        public TrackingMemoryStream Content { get; init; } = new([]);

        public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockSave { get; init; }

        public int GetCount { get; private set; }

        public string? LastProjectId { get; private set; }

        public string? LastApiKeyId { get; private set; }

        public async Task<FileRecord> SaveAsync(
            string? projectId,
            string apiKeyId,
            string fileName,
            string purpose,
            Stream content,
            long declaredLength,
            CancellationToken cancellationToken)
        {
            LastProjectId = projectId;
            LastApiKeyId = apiKeyId;
            SaveEntered.TrySetResult();
            if (BlockSave)
            {
                await ReleaseSave.Task.WaitAsync(cancellationToken);
            }

            return Record;
        }

        public Task<IReadOnlyList<FileRecord>> ListAsync(
            string? projectId,
            string apiKeyId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileRecord>>([Record]);

        public Task<FileRecord> GetRequiredAsync(
            string? projectId,
            string apiKeyId,
            string fileId,
            CancellationToken cancellationToken)
        {
            GetCount++;
            return Task.FromResult(Record);
        }

        public Task<(FileRecord Record, Stream Content)> OpenAsync(
            string? projectId,
            string apiKeyId,
            string fileId,
            CancellationToken cancellationToken) =>
            Task.FromResult((Record, (Stream)Content));

        public Task DeleteAsync(
            string? projectId,
            string apiKeyId,
            string fileId,
            CancellationToken cancellationToken)
        {
            DeleteEntered.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StubMcpDiscovery : IMcpMetadataDiscoveryService
    {
        public Task<IReadOnlyList<DiscoveredMcpServer>> DiscoverAsync(
            IReadOnlyList<ResolvedMcpServer> servers,
            CancellationToken cancellationToken)
        {
            var schema = JsonSerializer.SerializeToElement(new { type = "object" });
            IReadOnlyList<DiscoveredMcpServer> result =
            [
                new DiscoveredMcpServer(
                    "catalog",
                    new McpServerInfoMetadata("catalog", "1.0", "Catalog", "Tools", null, null),
                    [
                        Tool("write", schema),
                        Tool("hidden", schema),
                        Tool("read", schema)
                    ])
            ];
            return Task.FromResult(result);
        }

        private static McpToolMetadata Tool(string name, JsonElement schema) =>
            new(name, name, name + " tool", schema, null, null, null, null);
    }
}
