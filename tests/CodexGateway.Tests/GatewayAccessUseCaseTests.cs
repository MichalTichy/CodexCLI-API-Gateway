using CodexGateway.Infrastructure.Persistence;
using CodexGateway.Logic;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Logic.UseCases.Security;
using CodexGateway.Models;
using Microsoft.Extensions.Options;

namespace CodexGateway.Tests;

public sealed class GatewayAccessUseCaseTests
{
    [Fact]
    public async Task Authentication_accepts_only_an_exact_secret_and_returns_no_secret_metadata()
    {
        var repository = await CreateRepositoryAsync();
        var handler = new AuthenticateGatewayRequestUseCaseHandler(repository);

        Assert.Equal(
            "default",
            (await handler.Handle(
                new AuthenticateGatewayRequestUseCase("default-secret", null),
                CancellationToken.None))?.ApiKeyId);
        Assert.Null(await handler.Handle(
            new AuthenticateGatewayRequestUseCase(" default-secret ", null),
            CancellationToken.None));
        Assert.Null(await handler.Handle(
            new AuthenticateGatewayRequestUseCase("wrong-secret", null),
            CancellationToken.None));
        Assert.Null(typeof(GatewayRequestContext).GetProperty("ApiKey"));
    }

    [Fact]
    public async Task Http_mcp_environment_headers_are_normalized_and_validated()
    {
        var repository = await CreateRepositoryAsync();
        var handler = new CreateOrUpdateMcpServerUseCaseHandler(repository);
        var saved = Assert.IsType<HttpMcpServerDefinition>(await handler.Handle(
            new CreateOrUpdateMcpServerUseCase(new HttpMcpServerDefinition
            {
                Id = "header-auth",
                Name = "Header auth",
                Url = "https://mcp.example.test",
                EnvironmentHeaders = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = "MCP_API_KEY",
                    ["Authorization"] = "MCP_AUTHORIZATION"
                }
            }),
            CancellationToken.None));

        Assert.Equal("MCP_API_KEY", saved.EnvironmentHeaders["x-api-key"]);
        var invalid = await Assert.ThrowsAsync<GatewayException>(() => handler.Handle(
            new CreateOrUpdateMcpServerUseCase(saved with
            {
                EnvironmentHeaders = new Dictionary<string, string>
                {
                    ["Invalid Header"] = "MCP_API_KEY"
                }
            }),
            CancellationToken.None));
        Assert.Equal("environment_headers", invalid.Field);
    }

    [Fact]
    public async Task Project_access_and_mcp_tools_are_resolved_by_specifications()
    {
        var repository = await CreateRepositoryAsync();
        var options = TestOptions();
        var saveMcp = new CreateOrUpdateMcpServerUseCaseHandler(repository);
        await saveMcp.Handle(
            new CreateOrUpdateMcpServerUseCase(new HttpMcpServerDefinition
            {
                Id = "trusted-mcp",
                Name = "Trusted MCP",
                Enabled = true,
                Url = "https://mcp.example.test",
                AvailableTools = ["read", "write"]
            }),
            CancellationToken.None);
        var create = new CreateProjectUseCaseHandler(repository, new StubProjectStorage());
        var project = await create.Handle(
            new CreateProjectUseCase("project-one", "Project One"),
            CancellationToken.None);
        project = await new UpdateProjectUseCaseHandler(repository, new RunCoordinator(options)).Handle(
            new UpdateProjectUseCase(project with
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
            }),
            CancellationToken.None);

        Assert.Null(await repository.GetBySpecAsync(
            new ProjectAccessSpecification(project.Id, "default")));
        var access = await repository.GetBySpecAsync(
            new ProjectAccessSpecification(project.Id, "secondary"));
        Assert.NotNull(access);
        var mcp = Assert.Single((await repository.GetBySpecAsync(
            new EnabledMcpServersSpecification(access.Access)))!);
        Assert.True(mcp.Required);
        Assert.Equal(["read"], mcp.EnabledTools);
    }

    private static async Task<InMemoryGatewayStateRepository> CreateRepositoryAsync()
    {
        var repository = new InMemoryGatewayStateRepository();
        await repository.AddAsync(new GatewayState
        {
            ApiKeys =
            [
                new GatewayApiKeyDefinition { Id = "default", Name = "Default", Key = "default-secret" },
                new GatewayApiKeyDefinition { Id = "secondary", Name = "Secondary", Key = "secondary-secret" }
            ]
        });
        return repository;
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

    private sealed class StubProjectStorage : IProjectStorage
    {
        public void Create(string projectId)
        {
        }

        public void Delete(string projectId)
        {
        }
    }
}
