using CodexGateway.Logic.Errors;
using CodexGateway.Models;

namespace CodexGateway.Tests.Logic.UseCases;

public sealed class DeleteApiKeyUseCaseTests
{
    [Fact]
    public async Task Deleting_a_key_removes_all_its_grants_and_preserves_other_access()
    {
        var retainedKey = new ApiKeyDefinition { Id = "retained", Name = "Retained", Key = "retained-secret" };
        var retainedAccess = new ProjectApiKeyAccess
        {
            ApiKeyId = retainedKey.Id,
            WebSearchMode = WebSearchMode.Live,
            McpServers = [new ProjectMcpAssignment { ServerId = "mail", EnabledTools = ["read_mail"] }]
        };
        var repository = new FakeGatewayStateRepository(new GatewayState
        {
            ApiKeys =
            [
                new ApiKeyDefinition { Id = "target", Name = "Target", Key = "target-secret" },
                retainedKey
            ],
            Projects =
            [
                new ProjectDefinition
                {
                    Id = "first", Name = "First",
                    ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "target" }, retainedAccess]
                },
                new ProjectDefinition
                {
                    Id = "second", Name = "Second",
                    ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = "target" }]
                },
                new ProjectDefinition { Id = "unrelated", Name = "Unrelated" }
            ]
        });

        await new DeleteApiKeyUseCaseHandler(repository).Handle(
            new DeleteApiKeyUseCase("target"), CancellationToken.None);

        Assert.Equal(1, repository.UpdateCount);
        Assert.Equal(retainedKey, Assert.Single(repository.State.ApiKeys));
        Assert.Equal(3, repository.State.Projects.Count);
        Assert.Equal(retainedAccess, Assert.Single(repository.State.Projects[0].ApiKeyAccess));
        Assert.Empty(repository.State.Projects[1].ApiKeyAccess);
        Assert.Empty(repository.State.Projects[2].ApiKeyAccess);

        var recreated = await new CreateApiKeyUseCaseHandler(repository).Handle(
            new CreateApiKeyUseCase("target", "Replacement"), CancellationToken.None);

        Assert.NotEqual("target-secret", recreated.Key);
        Assert.All(repository.State.Projects, project =>
            Assert.DoesNotContain(project.ApiKeyAccess, access => access.ApiKeyId == recreated.Id));
    }

    [Fact]
    public async Task Deleting_an_unknown_key_returns_not_found_without_changing_state()
    {
        var key = new ApiKeyDefinition { Id = "retained", Name = "Retained", Key = "retained-secret" };
        var project = new ProjectDefinition
        {
            Id = "project", Name = "Project",
            ApiKeyAccess = [new ProjectApiKeyAccess { ApiKeyId = key.Id }]
        };
        var repository = new FakeGatewayStateRepository(new GatewayState
        {
            ApiKeys = [key], Projects = [project]
        });

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            new DeleteApiKeyUseCaseHandler(repository).Handle(
                new DeleteApiKeyUseCase("missing"), CancellationToken.None));

        Assert.Equal("api_key_not_found", exception.Code);
        Assert.Equal(404, exception.StatusCode);
        Assert.Equal(0, repository.UpdateCount);
        Assert.Equal(key, Assert.Single(repository.State.ApiKeys));
        Assert.Equal(project, Assert.Single(repository.State.Projects));
    }
}
