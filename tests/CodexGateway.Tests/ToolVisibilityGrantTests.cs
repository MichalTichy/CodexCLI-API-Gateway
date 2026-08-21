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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CodexGateway.Tests;

public sealed class ToolVisibilityGrantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codex-gateway-tool-visibility-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Missing_or_null_visible_tools_defaults_exactly_to_enabled_tools()
    {
        var services = CreateServices();
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        await File.WriteAllTextAsync(StateFile, """
            {
              "projects": [
                {
                  "id": "default-tools",
                  "name": "Default tools",
                  "enabled": true,
                  "apiKeyAccess": [
                    {
                      "apiKeyId": "default",
                      "mcpServers": [
                        {
                          "serverId": "catalog",
                          "required": true,
                          "enabledTools": ["write", "read"]
                        }
                      ]
                    },
                    {
                      "apiKeyId": "secondary",
                      "mcpServers": [
                        {
                          "serverId": "catalog",
                          "required": false,
                          "enabledTools": ["delete"],
                          "visibleTools": null
                        }
                      ]
                    }
                  ]
                }
              ],
              "mcpServers": [
                {
                  "id": "catalog",
                  "name": "Catalog",
                  "enabled": true,
                  "url": "https://mcp.example.test",
                  "availableTools": ["read", "write", "delete"]
                }
              ]
            }
            """);

        var project = Assert.Single((await services.Repository.QueryAsync(
            new GatewayStateSnapshotSpecification())).Projects);
        var missing = project.ApiKeyAccess.Single(access => access.ApiKeyId == "default");
        var explicitNull = project.ApiKeyAccess.Single(access => access.ApiKeyId == "secondary");
        Assert.Null(Assert.Single(missing.McpServers).VisibleTools);
        Assert.Null(Assert.Single(explicitNull.McpServers).VisibleTools);

        Assert.Equal(
            ["write", "read"],
            Assert.Single(await services.McpServers.ResolveVisibleAsync(missing, CancellationToken.None)).EnabledTools);
        Assert.Equal(
            ["delete"],
            Assert.Single(await services.McpServers.ResolveVisibleAsync(explicitNull, CancellationToken.None)).EnabledTools);
        Assert.Equal(
            ["write", "read"],
            Assert.Single(await services.McpServers.ResolveAsync(missing, CancellationToken.None)).EnabledTools);
        Assert.Equal(
            ["delete"],
            Assert.Single(await services.McpServers.ResolveAsync(explicitNull, CancellationToken.None)).EnabledTools);
    }

    [Fact]
    public async Task Explicit_visible_tools_may_exceed_enabled_but_runtime_invocation_never_expands()
    {
        var services = CreateServices();
        await AddTrustedCatalogAsync(services.SaveMcp);
        var project = await services.CreateProject.Handle(
            new CreateProjectUseCase("planned-tools", "Planned tools"),
            CancellationToken.None);

        var updated = await services.UpdateProject.Handle(new UpdateProjectUseCase(project with
        {
            ApiKeyAccess =
            [
                new ProjectApiKeyAccess
                {
                    ApiKeyId = "default",
                    McpServers =
                    [
                        new ProjectMcpAssignment
                        {
                            ServerId = "catalog",
                            Required = true,
                            EnabledTools = ["write"],
                            VisibleTools = ["write", "read", "delete"]
                        }
                    ]
                },
                new ProjectApiKeyAccess
                {
                    ApiKeyId = "secondary",
                    McpServers =
                    [
                        new ProjectMcpAssignment
                        {
                            ServerId = "catalog",
                            EnabledTools = ["read"],
                            VisibleTools = null
                        }
                    ]
                }
            ]
        }), CancellationToken.None);

        var defaultAccess = updated.ApiKeyAccess.Single(access => access.ApiKeyId == "default");
        var defaultGrant = Assert.Single(defaultAccess.McpServers);
        Assert.Equal(["write"], defaultGrant.EnabledTools);
        Assert.Equal(["delete", "read", "write"], defaultGrant.VisibleTools);
        Assert.Equal(
            ["write"],
            Assert.Single(await services.McpServers.ResolveAsync(defaultAccess, CancellationToken.None)).EnabledTools);
        Assert.Equal(
            ["delete", "read", "write"],
            Assert.Single(await services.McpServers.ResolveVisibleAsync(defaultAccess, CancellationToken.None)).EnabledTools);

        var secondaryAccess = updated.ApiKeyAccess.Single(access => access.ApiKeyId == "secondary");
        var secondaryGrant = Assert.Single(secondaryAccess.McpServers);
        Assert.Equal(["read"], secondaryGrant.EnabledTools);
        Assert.Equal(["read"], secondaryGrant.VisibleTools);
    }

    [Theory]
    [InlineData("duplicate-enabled", "enabled_tools")]
    [InlineData("unavailable-enabled", "enabled_tools")]
    [InlineData("blank-enabled", "enabled_tools")]
    [InlineData("duplicate-visible", "visible_tools")]
    [InlineData("unavailable-visible", "visible_tools")]
    [InlineData("enabled-not-visible", "visible_tools")]
    [InlineData("duplicate-server", "mcp_servers")]
    [InlineData("unavailable-server", "mcp_servers")]
    [InlineData("duplicate-key", "api_key_access")]
    [InlineData("unavailable-key", "api_key_access")]
    public async Task Invalid_duplicate_or_unavailable_grant_entries_are_rejected(
        string scenario,
        string expectedParameter)
    {
        var services = CreateServices();
        await AddTrustedCatalogAsync(services.SaveMcp);
        var project = await services.CreateProject.Handle(
            new CreateProjectUseCase("invalid-grant", "Invalid grant"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            services.UpdateProject.Handle(
                new UpdateProjectUseCase(project with
                {
                    ApiKeyAccess = InvalidAccess(scenario)
                }),
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(expectedParameter, exception.Field);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string StateFile => Path.Combine(_root, "data", "state.json");

    private TestServices CreateServices()
    {
        Directory.CreateDirectory(_root);
        var options = Options.Create(new GatewayOptions
        {
            ApiKeys =
            [
                new GlobalApiKeyOptions
                {
                    Id = "default",
                    Name = "Default",
                    Key = "default-secret"
                },
                new GlobalApiKeyOptions
                {
                    Id = "secondary",
                    Name = "Secondary",
                    Key = "secondary-secret"
                }
            ],
            StoragePath = Path.Combine(_root, "data")
        });
        var paths = new StoragePaths(options, new TestHostEnvironment(_root));
        var repository = new JsonGatewayConfigurationRepository(paths);
        var keys = new GlobalApiKeyService(options);
        var runs = new RunCoordinator(options);
        return new TestServices(
            repository,
            new McpServerResolver(repository),
            new CreateProjectUseCaseHandler(repository, new ProjectStorage(paths)),
            new UpdateProjectUseCaseHandler(repository, keys, runs),
            new CreateOrUpdateMcpServerUseCaseHandler(repository));
    }

    private static Task<McpServerDefinition> AddTrustedCatalogAsync(
        CreateOrUpdateMcpServerUseCaseHandler handler) =>
        handler.Handle(
            new CreateOrUpdateMcpServerUseCase(new McpServerDefinition
            {
                Id = "catalog",
                Name = "Catalog",
                Enabled = true,
                Transport = McpTransport.Http,
                Url = "https://mcp.example.test",
                AvailableTools = ["read", "write", "delete"]
            }),
            CancellationToken.None);

    private static List<ProjectApiKeyAccess> InvalidAccess(string scenario)
    {
        ProjectMcpAssignment Grant(
            string serverId = "catalog",
            string[]? enabled = null,
            string[]? visible = null) => new()
            {
                ServerId = serverId,
                EnabledTools = (enabled ?? ["read"]).ToList(),
                VisibleTools = (visible ?? ["read"]).ToList()
            };

        var grants = scenario switch
        {
            "duplicate-enabled" => new[] { Grant(enabled: ["read", "read"], visible: ["read"]) },
            "unavailable-enabled" => new[] { Grant(enabled: ["unknown"], visible: ["unknown"]) },
            "blank-enabled" => new[] { Grant(enabled: [""], visible: [""]) },
            "duplicate-visible" => new[] { Grant(visible: ["read", "read"]) },
            "unavailable-visible" => new[] { Grant(visible: ["read", "unknown"]) },
            "enabled-not-visible" => new[] { Grant(enabled: ["write"], visible: ["read"]) },
            "duplicate-server" => new[] { Grant(), Grant() },
            "unavailable-server" => new[] { Grant(serverId: "missing") },
            _ => new[] { Grant() }
        };
        var keyId = scenario == "unavailable-key" ? "missing" : "default";
        var result = new List<ProjectApiKeyAccess>
        {
            new() { ApiKeyId = keyId, McpServers = [.. grants] }
        };
        if (scenario == "duplicate-key")
        {
            result.Add(new ProjectApiKeyAccess { ApiKeyId = "default", McpServers = [] });
        }

        return result;
    }

    private sealed record TestServices(
        JsonGatewayConfigurationRepository Repository,
        McpServerResolver McpServers,
        CreateProjectUseCaseHandler CreateProject,
        UpdateProjectUseCaseHandler UpdateProject,
        CreateOrUpdateMcpServerUseCaseHandler SaveMcp);

    private sealed class GatewayStateSnapshotSpecification
        : ISpecification<GatewayState, GatewayState>
    {
        public GatewayState Apply(GatewayState source) => source;
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CodexGateway.Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
