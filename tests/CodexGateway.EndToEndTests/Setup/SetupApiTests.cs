using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.EndToEndTests.Infrastructure;

namespace CodexGateway.EndToEndTests.Setup;

public sealed class SetupApiTests
{
    [Fact]
    public async Task Setup_is_disabled_by_default_and_requires_its_separate_key()
    {
        await using var disabled = new GatewayFactory();
        using var disabledClient = disabled.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/admin/setup/state")).StatusCode);

        await using var enabled = new GatewayFactory(setupApiKey: "separate-setup-secret");
        using var client = enabled.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/setup/state")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "e2e-api-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/setup/state")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Setup-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/setup/state")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Setup-Key");
        client.DefaultRequestHeaders.Add("X-Setup-Key", "separate-setup-secret");
        using var response = await client.GetAsync("/admin/setup/state");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var state = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("e2e-api-key", state);
        Assert.DoesNotContain("e2e-secondary-api-key", state);
        Assert.DoesNotContain("separate-setup-secret", state);
    }

    [Fact]
    public async Task Repeated_setup_preserves_credentials_projects_and_unrelated_access()
    {
        await using var factory = new GatewayFactory(setupApiKey: "setup-secret");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Setup-Key", "setup-secret");

        var keyRequest = new { id = "setup-planning", name = "Samwise planning" };
        using var first = await client.PostAsJsonAsync("/admin/setup/api-keys", keyRequest);
        first.EnsureSuccessStatusCode();
        var created = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("created").GetBoolean());
        var secret = created.GetProperty("key").GetString();
        Assert.StartsWith("cg_", secret);

        using var second = await client.PostAsJsonAsync("/admin/setup/api-keys", keyRequest);
        second.EnsureSuccessStatusCode();
        var repeated = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(repeated.GetProperty("created").GetBoolean());
        Assert.Equal(JsonValueKind.Null, repeated.GetProperty("key").ValueKind);
        using var keyCollision = await client.PostAsJsonAsync("/admin/setup/api-keys", new { id = keyRequest.id, name = "Another app" });
        Assert.Equal(HttpStatusCode.Conflict, keyCollision.StatusCode);

        const string projectUrl = "/admin/setup/projects/setup-project";
        (await client.PutAsJsonAsync(projectUrl, new { name = "Samwise planning" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(projectUrl, new { name = "Samwise planning" })).EnsureSuccessStatusCode();
        using var projectCollision = await client.PutAsJsonAsync(projectUrl, new { name = "Another app" });
        Assert.Equal(HttpStatusCode.Conflict, projectCollision.StatusCode);

        (await client.PutAsJsonAsync(projectUrl + "/access/default", new { apiKeyId = "default", mcpServers = Array.Empty<object>() })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(projectUrl + "/access/setup-planning", new { apiKeyId = keyRequest.id, mcpServers = Array.Empty<object>() })).EnsureSuccessStatusCode();
        using var stateResponse = await client.GetAsync("/admin/setup/state");
        var state = await stateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var project = Assert.Single(state.GetProperty("projects").EnumerateArray());
        Assert.Equal(2, project.GetProperty("apiKeyAccess").GetArrayLength());
        Assert.DoesNotContain(secret!, state.ToString());

        // The created credential works, but cannot access a project without its own grant.
        using var credentialClient = factory.CreateClient();
        credentialClient.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        (await credentialClient.GetAsync("/p/setup-project/v1/tools")).EnsureSuccessStatusCode();
        await factory.CreateProjectAsync("unrelated-project", "Unrelated");
        Assert.Equal(HttpStatusCode.Unauthorized, (await credentialClient.GetAsync("/p/unrelated-project/v1/tools")).StatusCode);
    }

    [Fact]
    public async Task Mcp_registration_does_not_grant_tools_or_overwrite_existing_configuration()
    {
        await using var factory = new GatewayFactory(setupApiKey: "setup-secret");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Setup-Key", "setup-secret");
        var server = new { transport = "http", id = "test-server", name = "Test server", executionMode = 1,
            url = "http://localhost:9999/mcp", availableTools = new[] { "read_item" } };
        (await client.PutAsJsonAsync("/admin/setup/mcp-servers/test-server", server)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/admin/setup/mcp-servers/test-server", server)).EnsureSuccessStatusCode();
        var changed = new { transport = "http", id = "test-server", name = "Different server", executionMode = 1,
            url = "http://localhost:9999/mcp", availableTools = new[] { "read_item" } };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/admin/setup/mcp-servers/test-server", changed)).StatusCode);
        (await client.PutAsJsonAsync("/admin/setup/projects/test-project", new { name = "Test project" })).EnsureSuccessStatusCode();
        var invalidGrant = new { apiKeyId = "default", mcpServers = new[] { new { serverId = "test-server", enabledTools = new[] { "not_discovered" } } } };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/admin/setup/projects/test-project/access/default", invalidGrant)).StatusCode);
        var state = await client.GetFromJsonAsync<JsonElement>("/admin/setup/state");
        var project = Assert.Single(state.GetProperty("projects").EnumerateArray());
        Assert.Equal(0, project.GetProperty("apiKeyAccess").GetArrayLength());
    }
}
