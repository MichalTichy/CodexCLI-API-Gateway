using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests;

public sealed class GatewayApiTests : IDisposable
{
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;

    public GatewayApiTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Fact]
    public async Task Models_require_the_configured_bearer_key_and_expose_reasoning_levels()
    {
        var unauthorized = await _client.GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var unauthorizedJson = await unauthorized.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_api_key", unauthorizedJson.GetProperty("error").GetProperty("code").GetString());

        Authorize();
        var response = await _client.GetAsync("/v1/models");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var model = json.GetProperty("data").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "gpt-test-sol");
        Assert.Equal("medium", model.GetProperty("default_reasoning_effort").GetString());
        Assert.Equal(["low", "medium", "high"], model.GetProperty("supported_reasoning_efforts").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Unauthorized_openai_requests_challenge_with_bearer_and_generate_or_echo_request_ids()
    {
        using var generated = await _client.GetAsync("/v1/models");

        Assert.Equal(HttpStatusCode.Unauthorized, generated.StatusCode);
        Assert.Equal("Bearer", Assert.Single(generated.Headers.WwwAuthenticate).Scheme);
        var generatedRequestId = Assert.Single(generated.Headers.GetValues("x-request-id"));
        Assert.StartsWith("req_", generatedRequestId, StringComparison.Ordinal);
        Assert.Equal(36, generatedRequestId.Length);

        const string suppliedRequestId = "request-from-client";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.Add("x-request-id", suppliedRequestId);
        using var echoed = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, echoed.StatusCode);
        Assert.Equal("Bearer", Assert.Single(echoed.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(suppliedRequestId, Assert.Single(echoed.Headers.GetValues("x-request-id")));
    }

    [Fact]
    public async Task Chat_supports_non_streaming_and_openai_sse_streaming()
    {
        Authorize();
        var response = await SendChatAsync("/v1/chat/completions", "hello");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Fake Codex response", json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(13, json.GetProperty("usage").GetProperty("total_tokens").GetInt32());

        var streamResponse = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            reasoning_effort = "high",
            stream = true,
            messages = new[] { new { role = "user", content = "stream this" } }
        });
        streamResponse.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
        var stream = await streamResponse.Content.ReadAsStringAsync();
        Assert.Contains("Fake Codex response", stream, StringComparison.Ordinal);
        Assert.EndsWith("data: [DONE]\n\n", stream, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_failure_after_headers_emits_one_openai_error_event_followed_by_done()
    {
        Authorize();
        using var response = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            stream = true,
            messages = new[] { new { role = "user", content = "[scenario:fail]" } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var events = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("data: [DONE]", events[^1]);
        Assert.Single(events, value => string.Equals(value, "data: [DONE]", StringComparison.Ordinal));

        var jsonEvents = events
            .Select((value, index) => new { Value = value, Index = index })
            .Where(item => item.Value.StartsWith("data: {", StringComparison.Ordinal))
            .Select(item => new
            {
                item.Index,
                Json = JsonSerializer.Deserialize<JsonElement>(item.Value["data: ".Length..])
            })
            .ToArray();
        var errorEvent = Assert.Single(jsonEvents, item => item.Json.TryGetProperty("error", out _));
        Assert.Equal(events.Length - 2, errorEvent.Index);
        var error = errorEvent.Json.GetProperty("error");
        Assert.Equal("codex_failed", error.GetProperty("code").GetString());
        Assert.Equal("server_error", error.GetProperty("type").GetString());
        Assert.DoesNotContain("FAKE_SECRET", body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.RootPath, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Project_runs_persist_artifacts_while_projectless_runs_are_isolated()
    {
        Authorize();
        await CreateProjectAsync("accounting", "Accounting");

        (await SendChatAsync("/p/accounting/v1/chat/completions", "[scenario:write-artifact]")).EnsureSuccessStatusCode();
        var persisted = await SendChatAsync("/p/accounting/v1/chat/completions", "[scenario:read-artifact]");
        var persistedJson = await persisted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("persisted artifact", persistedJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());

        (await SendChatAsync("/v1/chat/completions", "[scenario:write-artifact]")).EnsureSuccessStatusCode();
        var isolated = await SendChatAsync("/v1/chat/completions", "[scenario:read-artifact]");
        var isolatedJson = await isolated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("artifact missing", isolatedJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public async Task Files_and_project_selected_mcp_tools_reach_the_real_process_boundary()
    {
        Authorize();
        await CreateProjectAsync("tools", "Tools");
        await _factory.UpsertMcpServerAsync(new HttpMcpServerDefinition
        {
            Id = "catalog",
            Name = "Catalog",
            Enabled = true,
            Url = "https://mcp.example.test",
            BearerTokenEnvironmentVariable = "GW_MCP_TEST_TOKEN",
            AvailableTools = ["read", "write", "delete"]
        });
        await _factory.UpsertMcpServerAsync(new StdioMcpServerDefinition
        {
            Id = "stdio-catalog",
            Name = "STDIO Catalog",
            Enabled = true,
            Command = "/opt/mcp/catalog-server",
            EnvironmentVariables = ["MCP_STDIO_TOKEN"],
            Arguments = ["--stdio", "argument with spaces"],
            AvailableTools = ["search"]
        });
        await _factory.UpdateProjectAsync(new ProjectDefinition
        {
            Id = "tools",
            Name = "Tools",
            Enabled = true,
            ApiKeyAccess =
            {
                new ProjectApiKeyAccess
                {
                    ApiKeyId = "default",
                    McpServers =
                    {
                        new ProjectMcpAssignment
                        {
                            ServerId = "catalog",
                            Required = true,
                            EnabledTools = ["read", "write"]
                        },
                        new ProjectMcpAssignment
                        {
                            ServerId = "stdio-catalog",
                            Required = true,
                            EnabledTools = ["search"]
                        }
                    }
                }
            }
        });

        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("assistants"), "purpose");
        upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("hello file")), "file", "notes.txt");
        var uploaded = await _client.PostAsync("/p/tools/v1/files", upload);
        uploaded.EnsureSuccessStatusCode();
        var uploadedJson = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = uploadedJson.GetProperty("id").GetString();

        var chat = await _client.PostAsJsonAsync("/p/tools/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            messages = new[] { new { role = "user", content = "[scenario:list-files]" } },
            file_ids = new[] { fileId }
        });
        chat.EnsureSuccessStatusCode();
        var chatJson = await chat.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("notes.txt", chatJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString(), StringComparison.Ordinal);

        var invocation = await ReadLastExecInvocationAsync();
        var arguments = invocation.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        var containerInvocation = await ReadLastContainerRunInvocationAsync();
        var containerArguments = containerInvocation.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        var imageIndex = Array.IndexOf(containerArguments, "codex-gateway-runner:e2e");
        Assert.True(imageIndex >= 0);
        Assert.Equal(arguments, containerArguments[(imageIndex + 1)..]);
        Assert.Contains("mcp_servers.catalog.enabled_tools=[\"read\",\"write\"]", arguments);
        Assert.Contains("mcp_servers.catalog.default_tools_approval_mode=\"approve\"", arguments);
        Assert.Contains(
            "mcp_servers.stdio_catalog.command=\"/opt/mcp/catalog-server\"",
            arguments);
        Assert.Contains(
            "mcp_servers.stdio_catalog.args=[\"--stdio\",\"argument with spaces\"]",
            arguments);
        Assert.Contains("mcp_servers.stdio_catalog.enabled_tools=[\"search\"]", arguments);
        Assert.Contains("--strict-config", arguments);
        Assert.Contains("--ignore-user-config", arguments);
        Assert.DoesNotContain("--output-last-message", arguments);
        Assert.DoesNotContain("--sandbox", arguments);
        Assert.Contains("default_permissions=\"gateway_run\"", arguments);
        Assert.Contains(
            "permissions.gateway_run.filesystem={\":minimal\"=\"read\",\":tmpdir\"=\"write\",\":slash_tmp\"=\"write\",\":workspace_roots\"=\"write\"}",
            arguments);
        Assert.Contains("permissions.gateway_run.network.enabled=false", arguments);
        Assert.Contains("shell_environment_policy.inherit=\"core\"", arguments);
        Assert.Contains("shell_environment_policy.ignore_default_excludes=false", arguments);
        Assert.Contains(arguments, value => value.StartsWith("shell_environment_policy.filters=", StringComparison.Ordinal));
        Assert.Contains(arguments, value => value.StartsWith("shell_environment_policy.set=", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, value => value.Contains("enabled_tools", StringComparison.Ordinal) && value.Contains("delete", StringComparison.Ordinal));
        Assert.DoesNotContain("GW_MCP_TEST_TOKEN=", string.Join(' ', arguments), StringComparison.Ordinal);
        Assert.DoesNotContain("GW_MCP_TEST_TOKEN=", string.Join(' ', containerArguments), StringComparison.Ordinal);
        Assert.DoesNotContain("MCP_STDIO_TOKEN=", string.Join(' ', containerArguments), StringComparison.Ordinal);
        Assert.DoesNotContain("[scenario:list-files]", string.Join(' ', arguments), StringComparison.Ordinal);
        Assert.DoesNotContain("[scenario:list-files]", string.Join(' ', containerArguments), StringComparison.Ordinal);
        Assert.Contains("[scenario:list-files]", invocation.GetProperty("stdin").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validation_and_cli_failures_use_safe_openai_errors()
    {
        Authorize();
        var invalid = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            reasoning_effort = "ultra",
            messages = new[] { new { role = "user", content = "hello" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidJson = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unsupported_reasoning_effort", invalidJson.GetProperty("error").GetProperty("code").GetString());

        var failed = await SendChatAsync("/v1/chat/completions", "[scenario:fail]");
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var body = await failed.Content.ReadAsStringAsync();
        Assert.Contains("codex_failed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE_SECRET", body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.RootPath, body, StringComparison.OrdinalIgnoreCase);

        var unavailable = await SendChatAsync("/v1/chat/completions", "[scenario:unavailable]");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        var unavailableJson = await unavailable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("codex_unavailable", unavailableJson.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disabled_projects_can_be_reenabled_and_bad_inputs_remain_scoped()
    {
        Authorize();
        await CreateProjectAsync("lifecycle", "Lifecycle");

        await _factory.UpdateProjectAsync(new ProjectDefinition
        {
            Id = "lifecycle",
            Name = "Lifecycle",
            Enabled = false,
            ApiKeyAccess =
            {
                new ProjectApiKeyAccess { ApiKeyId = "default" }
            }
        });
        var disabledProject = await _client.GetAsync("/p/lifecycle/v1/models");
        Assert.Equal(HttpStatusCode.Unauthorized, disabledProject.StatusCode);
        var disabledProjectJson = await disabledProject.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_api_key", disabledProjectJson.GetProperty("error").GetProperty("code").GetString());

        await _factory.UpdateProjectAsync(new ProjectDefinition
        {
            Id = "lifecycle",
            Name = "Lifecycle",
            Enabled = true,
            ApiKeyAccess =
            {
                new ProjectApiKeyAccess { ApiKeyId = "default" }
            }
        });
        (await _client.GetAsync("/p/lifecycle/v1/models")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/v1/files/not-a-file-id")).StatusCode);

        using var malformed = new StringContent("{", Encoding.UTF8, "application/json");
        var malformedResponse = await _client.PostAsync("/v1/chat/completions", malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        var malformedJson = await malformedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(malformedJson.TryGetProperty("error", out var malformedError), malformedJson.ToString());
        Assert.Equal("invalid_request", malformedError.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Malformed_chat_values_and_wrong_file_media_type_use_openai_errors()
    {
        Authorize();
        string[] bindingErrorBodies =
        [
            "{\"model\":42,\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}",
            "{\"model\":\"gpt-test-sol\",\"stream\":\"yes\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}",
            "{\"model\":\"gpt-test-sol\",\"messages\":\"not-an-array\"}"
        ];

        foreach (var body in bindingErrorBodies)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _client.PostAsync("/v1/chat/completions", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(json.TryGetProperty("error", out var error), json.ToString());
            Assert.Equal("The request is invalid.", error.GetProperty("message").GetString());
            Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Null, error.GetProperty("param").ValueKind);
            Assert.Equal("invalid_request", error.GetProperty("code").GetString());
        }

        string[] invalidBodies =
        [
            "{\"model\":\"gpt-test-sol\",\"messages\":null}",
            "{\"model\":\"gpt-test-sol\",\"messages\":[null]}",
            "{\"model\":\"gpt-test-sol\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}],\"file_ids\":[null]}",
            "{\"model\":\"gpt-test-sol\",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":42}]}]}",
            "{\"model\":\"gpt-test-sol\",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":42,\"text\":\"hello\"}]}]}",
            "{\"model\":\"gpt-test-sol\",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"file\",\"file_id\":42}]}]}"
        ];

        foreach (var body in invalidBodies)
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/v1/chat/completions", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(json.TryGetProperty("error", out var error), json.ToString());
            Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        }

        var wrongFileMediaType = await _client.PostAsJsonAsync("/v1/files", new { file = "not multipart" });
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongFileMediaType.StatusCode);
        var wrongFileJson = await wrongFileMediaType.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unsupported_media_type", wrongFileJson.GetProperty("error").GetProperty("code").GetString());

        using var missingBoundary = new ByteArrayContent([]);
        missingBoundary.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
        var missingBoundaryResponse = await _client.PostAsync("/v1/files", missingBoundary);
        Assert.Equal(HttpStatusCode.BadRequest, missingBoundaryResponse.StatusCode);
        var missingBoundaryJson = await missingBoundaryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_request", missingBoundaryJson.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Projectless_files_support_standard_nested_file_parts_and_remain_scope_bound()
    {
        Authorize();
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("assistants"), "purpose");
        upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("projectless file body")), "file", "input.txt");
        var uploaded = await _client.PostAsync("/v1/files", upload);
        uploaded.EnsureSuccessStatusCode();
        var uploadedJson = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = uploadedJson.GetProperty("id").GetString()!;

        var listed = await _client.GetFromJsonAsync<JsonElement>("/v1/files");
        Assert.Contains(listed.GetProperty("data").EnumerateArray(), file => file.GetProperty("id").GetString() == fileId);
        Assert.Equal("projectless file body", await _client.GetStringAsync($"/v1/files/{fileId}/content"));

        var referenced = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "[scenario:list-files]" },
                        new { type = "file", file = (object)new { file_id = fileId } }
                    }
                }
            }
        });
        referenced.EnsureSuccessStatusCode();
        var referencedJson = await referenced.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("input.txt", referencedJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString(), StringComparison.Ordinal);

        await CreateProjectAsync("file-scope", "File scope");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/p/file-scope/v1/files/{fileId}")).StatusCode);

        var wrongScopeReference = await _client.PostAsJsonAsync("/p/file-scope/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            messages = new[] { new { role = "user", content = "read it" } },
            file_ids = new[] { fileId }
        });
        Assert.Equal(HttpStatusCode.NotFound, wrongScopeReference.StatusCode);

        var deleted = await _client.DeleteAsync($"/v1/files/{fileId}");
        deleted.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/v1/files/{fileId}")).StatusCode);
    }

    [Fact]
    public async Task Device_login_blocks_runs_until_a_correlated_completion_or_cancel()
    {
        Authorize();
        var holdPath = Path.Combine(_factory.ScenarioPath, "hold-device-login");
        await File.WriteAllTextAsync(holdPath, string.Empty);
        var codex = _factory.GetCodexControlPlane();

        var started = await codex.StartDeviceLoginAsync(CancellationToken.None);
        Assert.Equal(DeviceLoginStatus.Pending, started.Status);
        Assert.Equal("TEST-CODE", started.UserCode);

        var blockedRun = await SendChatAsync("/v1/chat/completions", "while login is pending");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blockedRun.StatusCode);
        var blockedJson = await blockedRun.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("codex_unavailable", blockedJson.GetProperty("error").GetProperty("code").GetString());

        var duplicate = await Assert.ThrowsAsync<GatewayException>(() =>
            codex.StartDeviceLoginAsync(CancellationToken.None));
        Assert.Equal("login_in_progress", duplicate.Code);

        await codex.CancelDeviceLoginAsync(CancellationToken.None);
        (await SendChatAsync("/v1/chat/completions", "after login cancellation")).EnsureSuccessStatusCode();

        File.Delete(holdPath);
        await codex.StartDeviceLoginAsync(CancellationToken.None);
        await WaitUntilAsync(async () =>
        {
            var status = await codex.GetDeviceLoginAsync(CancellationToken.None);
            return status?.Status == DeviceLoginStatus.Completed;
        });

        await codex.LogoutAsync(CancellationToken.None);
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

    private void Authorize() => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");

    private Task<HttpResponseMessage> SendChatAsync(string path, string content) => _client.PostAsJsonAsync(path, new
    {
        model = "gpt-test-sol",
        messages = new[] { new { role = "user", content } }
    });

    private Task CreateProjectAsync(string id, string name) =>
        _factory.CreateProjectAsync(id, name);

    private async Task<JsonElement> ReadLastExecInvocationAsync()
    {
        return await ReadLastInvocationAsync(root => root.GetProperty("mode").GetString() == "exec");
    }

    private async Task<JsonElement> ReadLastContainerRunInvocationAsync()
    {
        return await ReadLastInvocationAsync(root =>
            root.GetProperty("mode").GetString() == "container-engine" &&
            root.GetProperty("arguments")[0].GetString() == "run");
    }

    private async Task<JsonElement> ReadLastInvocationAsync(Func<JsonElement, bool> predicate)
    {
        var directory = Path.Combine(_factory.ScenarioPath, "invocations");
        var path = Directory.EnumerateFiles(directory, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First(file =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                return predicate(document.RootElement);
            });
        await using var stream = File.OpenRead(path);
        return (await JsonDocument.ParseAsync(stream)).RootElement.Clone();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
