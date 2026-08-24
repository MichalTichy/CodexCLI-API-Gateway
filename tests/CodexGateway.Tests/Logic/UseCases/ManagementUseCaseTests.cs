using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Logic.UseCases.CodexAuthentication;
using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models;
using Microsoft.Extensions.Options;

namespace CodexGateway.Tests.Logic.UseCases;

public sealed class ManagementUseCaseTests
{
    [Fact]
    public async Task Create_api_key_generates_persists_and_returns_a_unique_secret()
    {
        var repository = new FakeGatewayStateRepository(new GatewayState());
        var handler = new CreateApiKeyUseCaseHandler(repository);

        var first = await handler.Handle(
            new CreateApiKeyUseCase(" First_Key ", " First key "),
            CancellationToken.None);
        var second = await handler.Handle(
            new CreateApiKeyUseCase("second-key", "Second key"),
            CancellationToken.None);

        Assert.Equal("first_key", first.Id);
        Assert.Equal("First key", first.Name);
        Assert.Matches("^cg_[0-9a-f]{64}$", first.Key);
        Assert.Matches("^cg_[0-9a-f]{64}$", second.Key);
        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal([first, second], repository.State.ApiKeys);

        var authenticated = await new AuthenticateGatewayRequestUseCaseHandler(repository).Handle(
            new AuthenticateGatewayRequestUseCase(first.Key, null),
            CancellationToken.None);
        Assert.Equal(first.Id, authenticated?.ApiKeyId);
    }

    [Fact]
    public async Task Read_specifications_apply_ordering_and_key_identities_exclude_secrets()
    {
        var repository = new FakeGatewayStateRepository(new GatewayState
        {
            ApiKeys =
            [
                new ApiKeyDefinition { Id = "default", Name = "Default", Key = "default-secret" },
                new ApiKeyDefinition { Id = "secondary", Name = "Secondary", Key = "secondary-secret" }
            ],
            Projects =
            [
                Project("z-project", "Z"),
                Project("a-project", "A")
            ],
            McpServers =
            [
                Server("z-server"),
                Server("a-server")
            ]
        });
        var projects = await repository.GetBySpecAsync(
            new ProjectsOrderedByIdSpecification(),
            CancellationToken.None);
        var servers = await repository.GetBySpecAsync(
            new McpServersOrderedByIdSpecification(),
            CancellationToken.None);
        var identities = await repository.GetBySpecAsync(
            new ApiKeysOrderedByIdSpecification(),
            CancellationToken.None);

        Assert.Equal(["a-project", "z-project"], projects!.Select(project => project.Id));
        Assert.Equal(["a-server", "z-server"], servers!.Select(server => server.Id));
        Assert.Equal(["default", "secondary"], identities!.Select(identity => identity.Id));
        Assert.Null(typeof(ApiKeyIdentity).GetProperty("Key"));
    }

    [Fact]
    public async Task Project_use_cases_preserve_created_at_and_manage_project_storage()
    {
        var repository = new FakeGatewayStateRepository(new GatewayState
        {
            ApiKeys = [new ApiKeyDefinition { Id = "default", Name = "Default", Key = "test-secret" }]
        });
        var storage = new RecordingProjectStorageManager();
        var options = TestOptions();
        var runs = new RunCoordinator(options);
        var create = new CreateProjectUseCaseHandler(repository, storage);
        var update = new UpdateProjectUseCaseHandler(repository, runs);
        var delete = new DeleteProjectUseCaseHandler(repository, storage, runs);

        var created = await create.Handle(
            new CreateProjectUseCase(" Project-One ", " Project One "),
            CancellationToken.None);
        var updated = await update.Handle(
            new UpdateProjectUseCase(created with
            {
                Name = " Renamed ",
                Enabled = false,
                RunnerImage = " project-runner:test ",
                ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "default" }]
            }),
            CancellationToken.None);
        await delete.Handle(new DeleteProjectUseCase(created.Id), CancellationToken.None);

        Assert.Equal("project-one", created.Id);
        Assert.Equal(["project-one"], storage.CreatedIds);
        Assert.Equal("Renamed", updated.Name);
        Assert.False(updated.Enabled);
        Assert.Equal("project-runner:test", updated.RunnerImage);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(["project-one"], storage.DeletedIds);
        Assert.Empty(repository.State.Projects);
    }

    [Fact]
    public async Task Updating_project_rejects_a_runner_image_that_looks_like_an_engine_option()
    {
        var repository = new FakeGatewayStateRepository(new GatewayState
        {
            Projects = [Project("project-one", "Project One")]
        });
        var handler = new UpdateProjectUseCaseHandler(
            repository,
            new RunCoordinator(TestOptions()));

        var exception = await Assert.ThrowsAsync<GatewayException>(() => handler.Handle(
            new UpdateProjectUseCase(new ProjectDefinition
            {
                Id = "project-one",
                Name = "Project One",
                RunnerImage = "--privileged"
            }),
            CancellationToken.None));

        Assert.Equal("invalid_request", exception.Code);
        Assert.Equal("runner_image", exception.Field);
    }

    [Fact]
    public async Task Delete_project_rejects_admitted_runs_before_mutating_repository_or_storage()
    {
        var options = TestOptions();
        var runs = new RunCoordinator(options);
        var repository = new FakeGatewayStateRepository(new GatewayState
        {
            Projects = [Project("busy-project", "Busy")]
        });
        var storage = new RecordingProjectStorageManager();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = runs.ExecuteAsync(
            "busy-project",
            async cancellationToken =>
            {
                started.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                return true;
            },
            CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var exception = await Assert.ThrowsAsync<GatewayException>(() =>
                new DeleteProjectUseCaseHandler(repository, storage, runs).Handle(
                    new DeleteProjectUseCase("busy-project"),
                    CancellationToken.None));

            Assert.Equal("runs_active", exception.Code);
            Assert.Equal(0, repository.UpdateCount);
            Assert.Empty(storage.DeletedIds);
        }
        finally
        {
            release.SetResult();
            await run;
        }
    }

    [Fact]
    public async Task Delete_mcp_server_removes_catalog_and_all_project_grants_in_one_update()
    {
        var target = Server("target");
        var retained = Server("retained");
        var repository = new FakeGatewayStateRepository(new GatewayState
        {
            McpServers = [target, retained],
            Projects =
            [
                Project("project-one", "Project One") with
                {
                    ApiKeyAccess =
                    [
                        new ProjectApiKeyAccess
                        {
                            ApiKeyId = "default",
                            McpServers = [Assignment("target"), Assignment("retained")]
                        },
                        new ProjectApiKeyAccess
                        {
                            ApiKeyId = "secondary",
                            McpServers = [Assignment("target")]
                        }
                    ]
                }
            ]
        });

        await new DeleteMcpServerUseCaseHandler(repository).Handle(
            new DeleteMcpServerUseCase("target"),
            CancellationToken.None);

        Assert.Equal(1, repository.UpdateCount);
        Assert.Equal(["retained"], repository.State.McpServers.Select(server => server.Id));
        var project = Assert.Single(repository.State.Projects);
        Assert.Equal(
            ["retained"],
            project.ApiKeyAccess.Single(access => access.ApiKeyId == "default")
                .McpServers.Select(assignment => assignment.ServerId));
        Assert.Empty(project.ApiKeyAccess.Single(access => access.ApiKeyId == "secondary").McpServers);
    }

    [Fact]
    public async Task Codex_authentication_use_cases_forward_control_plane_state_and_commands()
    {
        var login = new DeviceLogin(
            "login-one",
            "https://example.test/device",
            "CODE",
            DeviceLoginStatus.Pending);
        var controlPlane = new RecordingControlPlane
        {
            Account = new CodexAccountStatus(true, "chatgpt", "user@example.test"),
            Login = login
        };

        var state = await new GetCodexAuthenticationStateUseCaseHandler(controlPlane).Handle(
            new GetCodexAuthenticationStateUseCase(),
            CancellationToken.None);
        var started = await new StartCodexDeviceLoginUseCaseHandler(controlPlane).Handle(
            new StartCodexDeviceLoginUseCase(),
            CancellationToken.None);
        await new CancelCodexDeviceLoginUseCaseHandler(controlPlane).Handle(
            new CancelCodexDeviceLoginUseCase(),
            CancellationToken.None);
        await new LogoutCodexAccountUseCaseHandler(controlPlane).Handle(
            new LogoutCodexAccountUseCase(),
            CancellationToken.None);

        Assert.Equal(controlPlane.Account, state.Account);
        Assert.Equal(login, state.Login);
        Assert.Equal(login, started);
        Assert.Equal(1, controlPlane.AccountReads);
        Assert.Equal(1, controlPlane.LoginReads);
        Assert.Equal(1, controlPlane.LoginStarts);
        Assert.Equal(1, controlPlane.LoginCancellations);
        Assert.Equal(1, controlPlane.Logouts);
    }

    private static ProjectDefinition Project(string id, string name) => new()
    {
        Id = id,
        Name = name
    };

    private static McpServerDefinition Server(string id) => new HttpMcpServerDefinition
    {
        Id = id,
        Name = id,
        Url = "https://mcp.example.test"
    };

    private static ProjectMcpAssignment Assignment(string serverId) => new()
    {
        ServerId = serverId
    };

    private static IOptions<GatewayOptions> TestOptions() => Options.Create(new GatewayOptions
    {
        Limits = new RunLimitOptions
        {
            MaxConcurrent = 2,
            MaxQueued = 2,
            TimeoutSeconds = 30
        }
    });

    private sealed class RecordingProjectStorageManager : IProjectStorageManager
    {
        public List<string> CreatedIds { get; } = [];

        public List<string> DeletedIds { get; } = [];

        public void Create(string projectId) => CreatedIds.Add(projectId);

        public void Delete(string projectId) => DeletedIds.Add(projectId);
    }

    private sealed class RecordingControlPlane : ICodexControlPlane
    {
        public required CodexAccountStatus Account { get; init; }

        public required DeviceLogin Login { get; init; }

        public int AccountReads { get; private set; }

        public int LoginReads { get; private set; }

        public int LoginStarts { get; private set; }

        public int LoginCancellations { get; private set; }

        public int Logouts { get; private set; }

        public Task<IReadOnlyList<CodexModel>> GetModelsAsync(
            bool forceRefresh,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CodexAccountStatus> GetAccountAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AccountReads++;
            return Task.FromResult(Account);
        }

        public Task<DeviceLogin> StartDeviceLoginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoginStarts++;
            return Task.FromResult(Login);
        }

        public Task<DeviceLogin?> GetDeviceLoginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoginReads++;
            return Task.FromResult<DeviceLogin?>(Login);
        }

        public Task CancelDeviceLoginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoginCancellations++;
            return Task.CompletedTask;
        }

        public Task LogoutAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logouts++;
            return Task.CompletedTask;
        }
    }
}
