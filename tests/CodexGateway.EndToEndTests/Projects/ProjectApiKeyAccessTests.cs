using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using CodexGateway.Logic.Specifications;
using CodexGateway.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.EndToEndTests.Projects;

public sealed class ProjectApiKeyAccessTests : IDisposable
{
    private const string DefaultKey = "e2e-api-key";
    private const string SecondaryKey = "e2e-secondary-api-key";
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;

    public ProjectApiKeyAccessTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Fact]
    public async Task Project_path_and_header_are_equivalent_while_denials_do_not_reveal_project_existence()
    {
        await CreateProjectAsync("scope-one", "Scope one", true, Access("default"));
        await CreateProjectAsync("scope-two", "Scope two", true, Access("default"));
        await CreateProjectAsync("scope-disabled", "Scope disabled", false, Access("default"));

        using (var path = await SendAsync(HttpMethod.Get, "/p/scope-one/v1/models", DefaultKey))
        {
            path.EnsureSuccessStatusCode();
        }

        using (var header = await SendAsync(HttpMethod.Get, "/v1/models", DefaultKey, "scope-one"))
        {
            header.EnsureSuccessStatusCode();
        }

        using (var both = await SendAsync(HttpMethod.Get, "/p/scope-one/v1/models", DefaultKey, "scope-one"))
        {
            both.EnsureSuccessStatusCode();
        }

        using (var projectlessDefault = await SendAsync(HttpMethod.Get, "/v1/models", DefaultKey))
        using (var projectlessSecondary = await SendAsync(HttpMethod.Get, "/v1/models", SecondaryKey))
        {
            projectlessDefault.EnsureSuccessStatusCode();
            projectlessSecondary.EnsureSuccessStatusCode();
        }

        using var denied = await SendAsync(HttpMethod.Get, "/p/scope-one/v1/models", SecondaryKey);
        using var missing = await SendAsync(HttpMethod.Get, "/p/scope-missing/v1/models", DefaultKey);
        using var disabled = await SendAsync(HttpMethod.Get, "/p/scope-disabled/v1/models", DefaultKey);
        using var invalid = await SendAsync(HttpMethod.Get, "/p/scope-one/v1/models", "not-a-configured-key");
        var deniedError = await AssertInvalidApiKeyAsync(denied);
        Assert.Equal(deniedError, await AssertInvalidApiKeyAsync(missing));
        Assert.Equal(deniedError, await AssertInvalidApiKeyAsync(disabled));
        Assert.Equal(deniedError, await AssertInvalidApiKeyAsync(invalid));

        using var deniedByHeader = await SendAsync(HttpMethod.Get, "/v1/models", SecondaryKey, "scope-one");
        Assert.Equal(deniedError, await AssertInvalidApiKeyAsync(deniedByHeader));

        using var mismatch = await SendAsync(
            HttpMethod.Get,
            "/p/scope-one/v1/models",
            DefaultKey,
            "scope-two");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        var mismatchJson = await mismatch.Content.ReadFromJsonAsync<JsonElement>();
        var mismatchError = mismatchJson.GetProperty("error");
        Assert.Equal("project_mismatch", mismatchError.GetProperty("code").GetString());
        Assert.Equal("invalid_request_error", mismatchError.GetProperty("type").GetString());
        Assert.Equal("OpenAI-Project", mismatchError.GetProperty("param").GetString());

        using (var write = await SendChatAsync(
                   "/p/scope-one/v1/chat/completions",
                   DefaultKey,
                   "[scenario:write-artifact]"))
        {
            write.EnsureSuccessStatusCode();
        }

        using (var readThroughHeader = await SendChatAsync(
                   "/v1/chat/completions",
                   DefaultKey,
                   "[scenario:read-artifact]",
                   "scope-one"))
        {
            readThroughHeader.EnsureSuccessStatusCode();
            var completion = await readThroughHeader.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "persisted artifact",
                completion.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        }

        using (var projectlessRead = await SendChatAsync(
                   "/v1/chat/completions",
                   DefaultKey,
                   "[scenario:read-artifact]"))
        {
            projectlessRead.EnsureSuccessStatusCode();
            var completion = await projectlessRead.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "artifact missing",
                completion.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        }

        var fileId = await UploadAsync("/v1/files", DefaultKey, "scope-one");
        using (var fileThroughPath = await SendAsync(
                   HttpMethod.Get,
                   $"/p/scope-one/v1/files/{fileId}",
                   DefaultKey))
        {
            fileThroughPath.EnsureSuccessStatusCode();
        }

        using var wrongProjectFile = await SendAsync(
            HttpMethod.Get,
            $"/v1/files/{fileId}",
            DefaultKey,
            "scope-two");
        Assert.Equal(HttpStatusCode.NotFound, wrongProjectFile.StatusCode);
    }

    [Fact]
    public async Task Two_global_keys_receive_only_their_exact_project_mcp_tools_at_the_container_boundary()
    {
        await _factory.UpsertMcpServerAsync(new HttpMcpServerDefinition
        {
            Id = "catalog",
            Name = "Catalog",
            Enabled = true,
            Url = "https://mcp.example.test",
            EnvironmentHeaders = new Dictionary<string, string>
            {
                ["X-Api-Key"] = "GW_MCP_TEST_TOKEN"
            },
            AvailableTools = ["read", "write", "delete"]
        });

        await CreateProjectAsync(
            "key-tools",
            "Key tools",
            true,
            Access("default", Grant("catalog", "read")),
            Access("secondary", Grant("catalog", "write")));

        using (var first = await SendChatAsync(
                   "/p/key-tools/v1/chat/completions",
                   DefaultKey,
                   "[scenario:default-key-tools]"))
        {
            first.EnsureSuccessStatusCode();
        }

        using (var second = await SendChatAsync(
                   "/v1/chat/completions",
                   SecondaryKey,
                   "[scenario:secondary-key-tools]",
                   "key-tools"))
        {
            second.EnsureSuccessStatusCode();
        }

        var defaultInvocation = await ReadExecInvocationAsync("[scenario:default-key-tools]");
        var defaultArguments = ReadArguments(defaultInvocation);
        Assert.Contains("mcp_servers.catalog.enabled_tools=[\"read\"]", defaultArguments);
        Assert.DoesNotContain(defaultArguments, value =>
            value.StartsWith("mcp_servers.catalog.enabled_tools=", StringComparison.Ordinal) &&
            (value.Contains("write", StringComparison.Ordinal) || value.Contains("delete", StringComparison.Ordinal)));
        AssertContainerBoundary(defaultInvocation, defaultArguments);

        var secondaryInvocation = await ReadExecInvocationAsync("[scenario:secondary-key-tools]");
        var secondaryArguments = ReadArguments(secondaryInvocation);
        Assert.Contains("mcp_servers.catalog.enabled_tools=[\"write\"]", secondaryArguments);
        Assert.DoesNotContain(secondaryArguments, value =>
            value.StartsWith("mcp_servers.catalog.enabled_tools=", StringComparison.Ordinal) &&
            (value.Contains("read", StringComparison.Ordinal) || value.Contains("delete", StringComparison.Ordinal)));
        AssertContainerBoundary(secondaryInvocation, secondaryArguments);
    }

    [Fact]
    public async Task Typed_key_identities_and_projects_never_expose_api_key_secrets()
    {
        await CreateProjectAsync("metadata", "Metadata", true, Access("default"), Access("secondary"));

        var repository = _factory.Services.GetRequiredService<IReadOnlyRepository<GatewayState>>();
        var keys = (await repository.GetBySpecAsync(new ApiKeysOrderedByIdSpecification()))!;
        Assert.Equal(["default", "secondary"], keys.Select(key => key.Id));
        var keyBody = JsonSerializer.Serialize(keys);
        Assert.DoesNotContain(DefaultKey, keyBody, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondaryKey, keyBody, StringComparison.Ordinal);

        var projects = await repository.GetBySpecAsync(new ProjectsOrderedByIdSpecification());
        var projectsBody = JsonSerializer.Serialize(projects);
        Assert.DoesNotContain(DefaultKey, projectsBody, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondaryKey, projectsBody, StringComparison.Ordinal);

    }

    [Fact]
    public async Task Projectless_files_are_private_to_the_authenticated_global_api_key()
    {
        var defaultFileId = await UploadAsync("/v1/files", DefaultKey, null, "default.txt");
        var secondaryFileId = await UploadAsync("/v1/files", SecondaryKey, null, "secondary.txt");

        using (var defaultList = await SendAsync(HttpMethod.Get, "/v1/files", DefaultKey))
        using (var secondaryList = await SendAsync(HttpMethod.Get, "/v1/files", SecondaryKey))
        {
            defaultList.EnsureSuccessStatusCode();
            secondaryList.EnsureSuccessStatusCode();
            var defaultJson = await defaultList.Content.ReadFromJsonAsync<JsonElement>();
            var secondaryJson = await secondaryList.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(defaultJson.GetProperty("data").EnumerateArray(), FileWithId(defaultFileId));
            Assert.DoesNotContain(defaultJson.GetProperty("data").EnumerateArray(), FileWithId(secondaryFileId));
            Assert.Contains(secondaryJson.GetProperty("data").EnumerateArray(), FileWithId(secondaryFileId));
            Assert.DoesNotContain(secondaryJson.GetProperty("data").EnumerateArray(), FileWithId(defaultFileId));
        }

        await AssertFileNotFoundAsync(await SendAsync(HttpMethod.Get, $"/v1/files/{secondaryFileId}", DefaultKey));
        await AssertFileNotFoundAsync(await SendAsync(HttpMethod.Get, $"/v1/files/{defaultFileId}", SecondaryKey));
        await AssertFileNotFoundAsync(await SendAsync(HttpMethod.Get, $"/v1/files/{secondaryFileId}/content", DefaultKey));
        await AssertFileNotFoundAsync(await SendAsync(HttpMethod.Get, $"/v1/files/{defaultFileId}/content", SecondaryKey));

        await AssertFileNotFoundAsync(await SendAsync(HttpMethod.Delete, $"/v1/files/{secondaryFileId}", DefaultKey));
        await AssertFileNotFoundAsync(await SendAsync(HttpMethod.Delete, $"/v1/files/{defaultFileId}", SecondaryKey));
        using (var defaultStillExists = await SendAsync(HttpMethod.Get, $"/v1/files/{defaultFileId}", DefaultKey))
        using (var secondaryStillExists = await SendAsync(HttpMethod.Get, $"/v1/files/{secondaryFileId}", SecondaryKey))
        {
            defaultStillExists.EnsureSuccessStatusCode();
            secondaryStillExists.EnsureSuccessStatusCode();
        }

        await AssertFileNotFoundAsync(await SendFileChatAsync(DefaultKey, secondaryFileId));
        await AssertFileNotFoundAsync(await SendFileChatAsync(SecondaryKey, defaultFileId));

        using (var defaultReference = await SendFileChatAsync(DefaultKey, defaultFileId))
        using (var secondaryReference = await SendFileChatAsync(SecondaryKey, secondaryFileId))
        {
            defaultReference.EnsureSuccessStatusCode();
            secondaryReference.EnsureSuccessStatusCode();
            var defaultCompletion = await defaultReference.Content.ReadFromJsonAsync<JsonElement>();
            var secondaryCompletion = await secondaryReference.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(
                "default.txt",
                defaultCompletion.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString(),
                StringComparison.Ordinal);
            Assert.Contains(
                "secondary.txt",
                secondaryCompletion.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString(),
                StringComparison.Ordinal);
        }

        var defaultMetadataPath = Path.Combine(
            _factory.StoragePath,
            "temporary-files",
            "keys",
            "default",
            defaultFileId,
            "metadata.json");
        var secondaryMetadataPath = Path.Combine(
            _factory.StoragePath,
            "temporary-files",
            "keys",
            "secondary",
            secondaryFileId,
            "metadata.json");
        Assert.Equal("default", (await ReadJsonAsync(defaultMetadataPath)).GetProperty("apiKeyId").GetString());
        Assert.Equal("secondary", (await ReadJsonAsync(secondaryMetadataPath)).GetProperty("apiKeyId").GetString());
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Services.GetRequiredService<CodexAppServerClient>().Dispose();
        _factory.Dispose();
        for (var attempt = 0; attempt < 20 && Directory.Exists(_factory.RootPath); attempt++)
        {
            try
            {
                Directory.Delete(_factory.RootPath, true);
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }
    }

    private async Task CreateProjectAsync(
        string id,
        string name,
        bool enabled,
        params ProjectApiKeyAccess[] apiKeyAccess) =>
        await _factory.CreateProjectAsync(id, name, enabled, apiKeyAccess);

    private Task<HttpResponseMessage> SendChatAsync(
        string path,
        string key,
        string prompt,
        string? projectHeader = null) =>
        SendAsync(
            HttpMethod.Post,
            path,
            key,
            projectHeader,
            JsonContent.Create(new
            {
                model = "gpt-test-sol",
                messages = new[] { new { role = "user", content = prompt } }
            }));

    private async Task<string> UploadAsync(
        string path,
        string key,
        string? projectHeader,
        string fileName = "scope.txt")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("assistants"), "purpose");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("header scoped file")), "file", fileName);
        using var response = await SendAsync(HttpMethod.Post, path, key, projectHeader, form);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> SendFileChatAsync(string key, string fileId) =>
        SendAsync(
            HttpMethod.Post,
            "/v1/chat/completions",
            key,
            content: JsonContent.Create(new
            {
                model = "gpt-test-sol",
                messages = new[] { new { role = "user", content = "[scenario:list-files]" } },
                file_ids = new[] { fileId }
            }));

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string key,
        string? projectHeader = null,
        HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (projectHeader is not null)
        {
            request.Headers.Add("OpenAI-Project", projectHeader);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<string> AssertInvalidApiKeyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error");
        Assert.Equal("invalid_api_key", error.GetProperty("code").GetString());
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("param").ValueKind);
        return error.ToString();
    }

    private static async Task AssertFileNotFoundAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("file_not_found", json.GetProperty("error").GetProperty("code").GetString());
        }
    }

    private static Predicate<JsonElement> FileWithId(string fileId) =>
        file => file.GetProperty("id").GetString() == fileId;

    private static async Task<JsonElement> ReadJsonAsync(string path)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return document.RootElement.Clone();
    }

    private async Task<JsonElement> ReadExecInvocationAsync(string promptMarker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var directory = Path.Combine(_factory.ScenarioPath, "invocations");
        while (true)
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, timeout.Token));
                    var root = document.RootElement;
                    if (root.GetProperty("mode").GetString() == "exec" &&
                        root.GetProperty("stdin").GetString()?.Contains(promptMarker, StringComparison.Ordinal) == true)
                    {
                        return root.Clone();
                    }
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
            }

            await Task.Delay(20, timeout.Token);
        }
    }

    private void AssertContainerBoundary(JsonElement execInvocation, string[] innerArguments)
    {
        var processId = execInvocation.GetProperty("processId").GetInt32();
        var directory = Path.Combine(_factory.ScenarioPath, "invocations");
        var containerArguments = Directory.EnumerateFiles(directory, "*.json")
            .Select(File.ReadAllText)
            .Select(content => JsonDocument.Parse(content))
            .Where(document =>
                document.RootElement.GetProperty("mode").GetString() == "container-engine" &&
                document.RootElement.GetProperty("processId").GetInt32() == processId &&
                document.RootElement.GetProperty("arguments")[0].GetString() == "run")
            .Select(document =>
            {
                using (document)
                {
                    return document.RootElement.GetProperty("arguments")
                        .EnumerateArray()
                        .Select(value => value.GetString()!)
                        .ToArray();
                }
            })
            .Single();
        var imageIndex = Array.IndexOf(containerArguments, "codex-gateway-runner:e2e");
        Assert.True(imageIndex >= 0);
        Assert.Equal(innerArguments, containerArguments[(imageIndex + 1)..]);
    }

    private static string[] ReadArguments(JsonElement invocation) =>
        invocation.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static ProjectApiKeyAccess Access(string apiKeyId, params ProjectMcpAssignment[] grants) => new()
    {
        ApiKeyId = apiKeyId,
        McpServers = [.. grants]
    };

    private static ProjectMcpAssignment Grant(string serverId, params string[] enabledTools) => new()
    {
        ServerId = serverId,
        Required = true,
        EnabledTools = [.. enabledTools]
    };
}
