using System.Text.Json;
using CodexGateway.Infrastructure.Storage;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Projects;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CodexGateway.Tests;

public sealed class GlobalApiKeyServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codex-gateway-global-key-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Catalog_authenticates_exact_secrets_and_returns_only_non_secret_metadata()
    {
        var options = Options.Create(new GatewayOptions
        {
            ApiKeys =
            [
                new GlobalApiKeyOptions { Id = "default", Name = "Default key", Key = "default-secret" },
                new GlobalApiKeyOptions { Id = "secondary", Name = "Secondary key", Key = "secondary-secret" }
            ]
        });
        var service = new GlobalApiKeyService(options);

        Assert.Equal(
            [new GlobalApiKeyIdentity("default", "Default key"), new GlobalApiKeyIdentity("secondary", "Secondary key")],
            service.List());
        Assert.Equal("default", service.Authenticate("default-secret")?.Id);
        Assert.Equal("secondary", service.Authenticate("secondary-secret")?.Id);
        Assert.Null(service.Authenticate(" secondary-secret "));
        Assert.Null(service.Authenticate("wrong-secret"));
        Assert.Null(service.Authenticate(null));
        Assert.Equal("Secondary key", service.FindById("secondary")?.Name);
        Assert.Null(service.FindById("SECONDARY"));
    }

    [Fact]
    public void Configuration_rejects_duplicate_ids_and_secrets()
    {
        Assert.False(GlobalApiKeyService.IsValidConfiguration(new GatewayOptions
        {
            ApiKeys =
            [
                new GlobalApiKeyOptions { Id = "default", Name = "Default", Key = "same-secret" },
                new GlobalApiKeyOptions { Id = "default", Name = "Duplicate default", Key = "other" }
            ]
        }));
        Assert.False(GlobalApiKeyService.IsValidConfiguration(new GatewayOptions
        {
            ApiKeys =
            [
                new GlobalApiKeyOptions { Id = "default", Name = "Default", Key = "same-secret" },
                new GlobalApiKeyOptions { Id = "secondary", Name = "Secondary", Key = "same-secret" }
            ]
        }));
    }

    [Fact]
    public async Task Project_access_is_explicit_per_global_key_and_validated_against_the_catalog()
    {
        var services = CreateServices();
        await services.SaveMcp.Handle(
            new CreateOrUpdateMcpServerUseCase(new HttpMcpServerDefinition
            {
                Id = "trusted-mcp",
                Name = "Trusted MCP",
                Enabled = true,
                Url = "https://mcp.example.test",
                AvailableTools = ["read", "write"]
            }),
            CancellationToken.None);
        var project = await services.CreateProject.Handle(
            new CreateProjectUseCase("project-one", "Project One"),
            CancellationToken.None);
        project = await services.UpdateProject.Handle(new UpdateProjectUseCase(project with
        {
            ApiKeyAccess =
            [
                new ProjectApiKeyAccess
                {
                    ApiKeyId = "secondary",
                    McpServers =
                    [
                        new ProjectMcpAssignment
                        {
                            ServerId = "trusted-mcp",
                            Required = true,
                            EnabledTools = ["read"]
                        }
                    ]
                }
            ]
        }), CancellationToken.None);

        Assert.Null(await services.Projects.ResolveAccessAsync(project.Id, "default", CancellationToken.None));
        var resolved = await services.Projects.ResolveAccessAsync(project.Id, "secondary", CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.Equal("secondary", resolved.Access.ApiKeyId);
        var mcp = Assert.Single(await services.McpServers.ResolveAsync(resolved.Access, CancellationToken.None));
        Assert.True(mcp.Required);
        Assert.Equal(["read"], mcp.EnabledTools);

        var invalid = project with
        {
            ApiKeyAccess =
            [
                new ProjectApiKeyAccess
                {
                    ApiKeyId = "secondary",
                    McpServers =
                    [
                        new ProjectMcpAssignment { ServerId = "trusted-mcp", EnabledTools = ["future-tool"] }
                    ]
                }
            ]
        };
        await Assert.ThrowsAsync<GatewayException>(() =>
            services.UpdateProject.Handle(new UpdateProjectUseCase(invalid), CancellationToken.None));
    }

    [Fact]
    public async Task Null_access_is_normalized_to_no_project_grants()
    {
        var options = Options.Create(CreateOptions());
        var paths = new StoragePaths(options, new KeyTestHostEnvironment(_root));
        Directory.CreateDirectory(paths.Root);
        await File.WriteAllTextAsync(paths.StateFile, """
            {
              "projects": [
                {
                  "id": "null-access",
                  "name": "Null Access",
                  "enabled": true,
                  "apiKeyAccess": null
                }
              ],
              "mcpServers": null
            }
            """);

        var state = await new JsonGatewayConfigurationRepository(paths).QueryAsync(
            new GatewayStateSnapshotSpecification());

        Assert.Empty(Assert.Single(state.Projects).ApiKeyAccess);
        Assert.Empty(state.McpServers);
    }

    [Fact]
    public async Task State_with_removed_fields_is_rejected()
    {
        var options = Options.Create(CreateOptions());
        var paths = new StoragePaths(options, new KeyTestHostEnvironment(_root));
        Directory.CreateDirectory(paths.Root);
        await File.WriteAllTextAsync(paths.StateFile, """
            {
              "version": 2,
              "projects": [],
              "mcpServers": []
            }
            """);

        await Assert.ThrowsAsync<JsonException>(() =>
            new JsonGatewayConfigurationRepository(paths).QueryAsync(
                new GatewayStateSnapshotSpecification()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private TestServices CreateServices()
    {
        Directory.CreateDirectory(_root);
        var options = Options.Create(CreateOptions());
        var paths = new StoragePaths(options, new KeyTestHostEnvironment(_root));
        var repository = new JsonGatewayConfigurationRepository(paths);
        var keys = new GlobalApiKeyService(options);
        var projects = new ProjectAccessResolver(repository, keys);
        var mcpServers = new McpServerResolver(repository);
        var runs = new RunCoordinator(options);
        return new TestServices(
            projects,
            mcpServers,
            new CreateProjectUseCaseHandler(repository, new ProjectStorage(paths)),
            new UpdateProjectUseCaseHandler(repository, keys, runs),
            new CreateOrUpdateMcpServerUseCaseHandler(repository));
    }

    private GatewayOptions CreateOptions() => new()
    {
        ApiKeys =
        [
            new GlobalApiKeyOptions { Id = "default", Name = "Default", Key = "default-secret" },
            new GlobalApiKeyOptions { Id = "secondary", Name = "Secondary", Key = "secondary-secret" }
        ],
        StoragePath = Path.Combine(_root, "data")
    };

    private sealed record TestServices(
        ProjectAccessResolver Projects,
        McpServerResolver McpServers,
        CreateProjectUseCaseHandler CreateProject,
        UpdateProjectUseCaseHandler UpdateProject,
        CreateOrUpdateMcpServerUseCaseHandler SaveMcp);

    private sealed class GatewayStateSnapshotSpecification
        : ISpecification<GatewayState, GatewayState>
    {
        public GatewayState Apply(GatewayState source) => source;
    }

    private sealed class KeyTestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CodexGateway.Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
