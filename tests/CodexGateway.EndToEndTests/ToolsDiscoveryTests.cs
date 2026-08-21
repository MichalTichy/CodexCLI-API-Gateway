using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using CodexGateway.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests;

public sealed class ToolsDiscoveryTests : IDisposable
{
    private const string DefaultKey = "e2e-api-key";
    private const string SecondaryKey = "e2e-secondary-api-key";
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;

    public ToolsDiscoveryTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Fact]
    public async Task Projectless_default_catalog_and_full_alias_are_identical_and_empty_for_every_valid_global_key()
    {
        using var defaultResponse = await SendAsync("/v1/tools", DefaultKey);
        using var secondaryResponse = await SendAsync("/v1/tools", SecondaryKey);
        using var defaultAliasResponse = await SendAsync("/v1/tools?detail=full", DefaultKey);
        using var secondaryAliasResponse = await SendAsync("/v1/tools?detail=full", SecondaryKey);

        var defaultCatalog = await AssertCatalogPayloadAsync(defaultResponse);
        var secondaryCatalog = await AssertCatalogPayloadAsync(secondaryResponse);
        var defaultAlias = await AssertCatalogPayloadAsync(defaultAliasResponse);
        var secondaryAlias = await AssertCatalogPayloadAsync(secondaryAliasResponse);
        Assert.Empty(defaultCatalog.GetProperty("data").EnumerateArray());
        Assert.Empty(secondaryCatalog.GetProperty("data").EnumerateArray());
        Assert.Equal(defaultCatalog.ToString(), defaultAlias.ToString());
        Assert.Equal(secondaryCatalog.ToString(), secondaryAlias.ToString());
        Assert.Equal(
            defaultCatalog.GetProperty("catalog_version").GetString(),
            secondaryCatalog.GetProperty("catalog_version").GetString());
        AssertNoDiscoveryContainerRuns();
    }

    [Fact]
    public async Task Default_catalog_and_full_alias_return_exact_enabled_metadata()
    {
        await _factory.UpsertMcpServerAsync(new StdioMcpServerDefinition
        {
            Id = "zeta",
            Name = "Zeta",
            Enabled = true,
            Command = "secret-zeta-command",
            Arguments = ["--secret-zeta-argument"],
            EnvironmentVariables = ["TOOLS_DISCOVERY_STDIO_SECRET"],
            AvailableTools = ["mutate", "lookup"]
        });
        await _factory.UpsertMcpServerAsync(new HttpMcpServerDefinition
        {
            Id = "alpha",
            Name = "Alpha",
            Enabled = true,
            Url = "https://config-secret.example.test/mcp?token=secret-query-value",
            BearerTokenEnvironmentVariable = "TOOLS_DISCOVERY_HTTP_SECRET",
            AvailableTools = ["write", "delete", "read"]
        });
        await CreateProjectAsync(
            "discover-tools",
            Access(
                "default",
                Grant("zeta", "lookup"),
                OptionalGrant("alpha", "read")),
            Access(
                "secondary",
                Grant("alpha", "write", "delete")));

        using var defaultPathResponse = await SendAsync("/p/discover-tools/v1/tools", DefaultKey);
        using var defaultHeaderResponse = await SendAsync("/v1/tools", DefaultKey, "discover-tools");
        using var secondaryDefaultResponse = await SendAsync("/v1/tools", SecondaryKey, "discover-tools");
        var defaultPath = await AssertCatalogPayloadAsync(defaultPathResponse);
        var defaultHeader = await AssertCatalogPayloadAsync(defaultHeaderResponse);
        var secondaryDefault = await AssertCatalogPayloadAsync(secondaryDefaultResponse);
        Assert.Equal(defaultPath.ToString(), defaultHeader.ToString());

        using var fullPathResponse = await SendAsync("/p/discover-tools/v1/tools?detail=full", DefaultKey);
        using var fullHeaderResponse = await SendAsync("/v1/tools?detail=full", DefaultKey, "discover-tools");
        using var fullBothResponse = await SendAsync(
            "/p/discover-tools/v1/tools?detail=full",
            DefaultKey,
            "discover-tools");
        using var secondaryFullResponse = await SendAsync(
            "/v1/tools?detail=full",
            SecondaryKey,
            "discover-tools");

        var fullPath = await AssertCatalogPayloadAsync(fullPathResponse);
        var fullHeader = await AssertCatalogPayloadAsync(fullHeaderResponse);
        var fullBoth = await AssertCatalogPayloadAsync(fullBothResponse);
        var secondaryFull = await AssertCatalogPayloadAsync(secondaryFullResponse);
        Assert.Equal(defaultPath.ToString(), fullPath.ToString());
        Assert.Equal(secondaryDefault.ToString(), secondaryFull.ToString());
        Assert.Equal(fullPath.ToString(), fullHeader.ToString());
        Assert.Equal(fullPath.ToString(), fullBoth.ToString());
        Assert.NotEqual(
            fullPath.GetProperty("catalog_version").GetString(),
            secondaryFull.GetProperty("catalog_version").GetString());

        AssertDefaultKeyMetadata(defaultPath);
        AssertSecondaryKeyMetadata(secondaryDefault);

        foreach (var payload in new[]
                 {
                     defaultPath, defaultHeader, secondaryDefault, fullPath, fullHeader, fullBoth, secondaryFull
                 })
        {
            var serialized = payload.ToString();
            Assert.DoesNotContain("secret-zeta-command", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-zeta-argument", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("config-secret.example.test", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-query-value", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("TOOLS_DISCOVERY_STDIO_SECRET", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("TOOLS_DISCOVERY_HTTP_SECRET", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"transport\"", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"command\"", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"arguments\"", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"environment_variables\"", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"bearer_token_environment_variable\"", serialized, StringComparison.Ordinal);
        }

        await AssertDiscoveryCrossedContainerBoundaryAndCleanedUpAsync();
    }

    [Fact]
    public async Task Ungranted_key_receives_the_generic_invalid_api_key_response()
    {
        await CreateProjectAsync("private-tools", Access("default"));

        using var deniedByPath = await SendAsync("/p/private-tools/v1/tools", SecondaryKey);
        using var deniedByHeader = await SendAsync("/v1/tools", SecondaryKey, "private-tools");
        using var missingProject = await SendAsync("/p/missing-tools/v1/tools", SecondaryKey);

        var deniedError = await AssertInvalidApiKeyAsync(deniedByPath);
        Assert.Equal(deniedError, await AssertInvalidApiKeyAsync(deniedByHeader));
        Assert.Equal(deniedError, await AssertInvalidApiKeyAsync(missingProject));
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

    private Task CreateProjectAsync(string id, params ProjectApiKeyAccess[] apiKeyAccess) =>
        _factory.CreateProjectAsync(id, id, apiKeyAccess: apiKeyAccess);

    private async Task<HttpResponseMessage> SendAsync(
        string path,
        string key,
        string? projectHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (projectHeader is not null)
        {
            request.Headers.Add("OpenAI-Project", projectHeader);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> AssertCatalogPayloadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertPropertyNames(payload, "object", "catalog_version", "data");
        Assert.Equal("list", payload.GetProperty("object").GetString());
        var catalogVersion = payload.GetProperty("catalog_version").GetString();
        Assert.NotNull(catalogVersion);
        Assert.StartsWith("sha256:", catalogVersion, StringComparison.Ordinal);
        Assert.Equal("sha256:".Length + 64, catalogVersion.Length);
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("data").ValueKind);
        return payload;
    }

    private static void AssertDefaultKeyMetadata(JsonElement payload)
    {
        var servers = payload.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, servers.Length);

        var alpha = servers[0];
        AssertServer(alpha, "alpha", "Alpha", "1.2.3", required: false, hasOptionalMetadata: true);
        Assert.Equal("Alpha Runtime", alpha.GetProperty("title").GetString());
        Assert.Equal("Alpha server description", alpha.GetProperty("description").GetString());
        Assert.Equal("https://metadata.example.test/alpha", alpha.GetProperty("website_url").GetString());
        AssertJsonEquals(
            """
            [
              {
                "src": "https://metadata.example.test/alpha.svg",
                "mimeType": "image/svg+xml",
                "sizes": ["any"],
                "theme": "light",
                "extension": { "preserved": true }
              }
            ]
            """,
            alpha.GetProperty("icons"));

        var alphaTools = alpha.GetProperty("tools").EnumerateArray().ToArray();
        var read = Assert.Single(alphaTools);
        AssertReadTool(read);

        var zeta = servers[1];
        AssertServer(zeta, "zeta", "Zeta", "9.8.7", required: true, hasOptionalMetadata: false);

        var zetaTools = zeta.GetProperty("tools").EnumerateArray().ToArray();
        var lookup = Assert.Single(zetaTools);
        Assert.Equal("lookup", ToolName(lookup));
        AssertPropertyNames(lookup, "id", "name", "description", "input_schema", "annotations");
        Assert.Equal("zeta/lookup", lookup.GetProperty("id").GetString());
        Assert.Equal("Looks up a value.", lookup.GetProperty("description").GetString());
        AssertJsonEquals(
            """
            {
              "oneOf": [
                { "type": "string" },
                { "type": "object", "additionalProperties": true }
              ]
            }
            """,
            lookup.GetProperty("input_schema"));
        AssertJsonEquals(
            """{ "readOnlyHint": true }""",
            lookup.GetProperty("annotations"));
    }

    private static void AssertSecondaryKeyMetadata(JsonElement payload)
    {
        var alpha = Assert.Single(payload.GetProperty("data").EnumerateArray());
        AssertServer(alpha, "alpha", "Alpha", "1.2.3", required: true, hasOptionalMetadata: true);
        var tools = alpha.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(new[] { "delete", "write" }, tools.Select(ToolName).ToArray());

        var delete = tools[0];
        Assert.Equal("delete", ToolName(delete));
        AssertPropertyNames(delete, "id", "name", "title", "description", "input_schema");
        Assert.Equal("alpha/delete", delete.GetProperty("id").GetString());
        Assert.Equal("Delete record", delete.GetProperty("title").GetString());
        Assert.Equal("Deletes one record.", delete.GetProperty("description").GetString());
        AssertJsonEquals(
            """
            {
              "type": "object",
              "properties": { "id": { "type": "string" } },
              "required": ["id"]
            }
            """,
            delete.GetProperty("input_schema"));

        AssertWriteTool(tools[1]);
    }

    private static void AssertServer(
        JsonElement server,
        string id,
        string name,
        string version,
        bool required,
        bool hasOptionalMetadata)
    {
        AssertPropertyNames(
            server,
            hasOptionalMetadata
                ? ["id", "name", "version", "required", "title", "description", "website_url", "icons", "tools"]
                : ["id", "name", "version", "required", "tools"]);
        Assert.Equal(id, server.GetProperty("id").GetString());
        Assert.Equal(name, server.GetProperty("name").GetString());
        Assert.Equal(version, server.GetProperty("version").GetString());
        Assert.Equal(required, server.GetProperty("required").GetBoolean());
        Assert.Equal(JsonValueKind.Array, server.GetProperty("tools").ValueKind);
    }

    private static void AssertReadTool(JsonElement tool)
    {
        Assert.Equal("read", ToolName(tool));
        AssertPropertyNames(tool, "id", "name", "input_schema");
        Assert.Equal("alpha/read", tool.GetProperty("id").GetString());
        AssertJsonEquals(
            """
            {
              "type": "object",
              "properties": { "id": { "type": ["string", "integer"] } }
            }
            """,
            tool.GetProperty("input_schema"));
    }

    private static void AssertWriteTool(JsonElement tool)
    {
        Assert.Equal("write", ToolName(tool));
        AssertPropertyNames(
            tool,
            "id",
            "name",
            "title",
            "description",
            "input_schema",
            "output_schema",
            "annotations",
            "icons",
            "_meta");
        Assert.Equal("alpha/write", tool.GetProperty("id").GetString());
        Assert.Equal("Write record", tool.GetProperty("title").GetString());
        Assert.Equal("Writes one record.", tool.GetProperty("description").GetString());
        AssertJsonEquals(
            """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "value": { "type": "string", "minLength": 1 },
                "options": {
                  "type": "object",
                  "additionalProperties": { "type": "boolean" }
                }
              },
              "required": ["value"],
              "additionalProperties": false
            }
            """,
            tool.GetProperty("input_schema"));
        AssertJsonEquals(
            """
            {
              "type": "object",
              "properties": { "record_id": { "type": "string" } },
              "required": ["record_id"]
            }
            """,
            tool.GetProperty("output_schema"));
        AssertJsonEquals(
            """
            {
              "readOnlyHint": false,
              "destructiveHint": true,
              "extension": { "risk": 7 }
            }
            """,
            tool.GetProperty("annotations"));
        AssertJsonEquals(
            """
            [
              {
                "src": "data:image/svg+xml;base64,PHN2Zy8+",
                "mimeType": "image/svg+xml",
                "extension": ["preserve", 42]
              }
            ]
            """,
            tool.GetProperty("icons"));
        AssertJsonEquals(
            """
            {
              "vendor.example/render": { "template": "record-card", "enabled": true },
              "arbitrary": [1, null, { "nested": "value" }]
            }
            """,
            tool.GetProperty("_meta"));
    }

    private void AssertNoDiscoveryContainerRuns() => Assert.DoesNotContain(
        ReadInvocations(),
        invocation => IsEngineCommand(invocation, "run"));

    private async Task AssertDiscoveryCrossedContainerBoundaryAndCleanedUpAsync()
    {
        var invocations = await WaitForInvocationsAsync(
            records => records.Any(record => IsEngineCommand(record, "run")) &&
                       records.Count(record => IsEngineCommand(record, "rm")) >=
                       records.Count(record => IsEngineCommand(record, "run")));
        var runs = invocations.Where(record => IsEngineCommand(record, "run")).ToArray();
        var removals = invocations.Where(record => IsEngineCommand(record, "rm")).ToArray();
        var appServers = invocations.Where(record => record.Mode == "app-server").ToArray();

        Assert.NotEmpty(runs);
        Assert.True(removals.Length >= runs.Length);
        foreach (var run in runs)
        {
            Assert.Contains("app-server", run.Arguments);
            Assert.Contains(appServers, appServer => appServer.ProcessId == run.ProcessId);
            var name = RequiredOption(run.Arguments, "--name");
            Assert.Contains(
                removals,
                removal => removal.Arguments.Contains(name, StringComparer.Ordinal) &&
                           (removal.Arguments.Contains("--force", StringComparer.Ordinal) ||
                            removal.Arguments.Contains("-f", StringComparer.Ordinal)));
        }

        var containerState = Path.Combine(_factory.ScenarioPath, "containers");
        Assert.False(
            Directory.Exists(containerState) && Directory.EnumerateFiles(containerState, "*.json").Any(),
            "Every metadata-discovery container must be removed.");
        var runWorkspaces = Path.Combine(_factory.StoragePath, "runs");
        Assert.False(
            Directory.Exists(runWorkspaces) && Directory.EnumerateFileSystemEntries(runWorkspaces, "run_*").Any(),
            "Every metadata-discovery workspace must be deleted.");
    }

    private async Task<IReadOnlyList<Invocation>> WaitForInvocationsAsync(
        Func<IReadOnlyList<Invocation>, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var invocations = ReadInvocations();
            if (condition(invocations))
            {
                return invocations;
            }

            await Task.Delay(20, timeout.Token);
        }
    }

    private IReadOnlyList<Invocation> ReadInvocations()
    {
        var directory = Path.Combine(_factory.ScenarioPath, "invocations");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<Invocation>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                result.Add(new Invocation(
                    root.GetProperty("mode").GetString()!,
                    root.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                    root.GetProperty("processId").GetInt32()));
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static bool IsEngineCommand(Invocation invocation, string command) =>
        invocation.Mode == "container-engine" && invocation.Arguments.FirstOrDefault() == command;

    private static string RequiredOption(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index] == name)
            {
                return arguments[index + 1];
            }
        }

        throw new Xunit.Sdk.XunitException($"Missing required option '{name}'.");
    }

    private static string ToolName(JsonElement tool) => tool.GetProperty("name").GetString()!;

    private static void AssertPropertyNames(JsonElement element, params string[] expected) => Assert.Equal(
        expected.Order(StringComparer.Ordinal).ToArray(),
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

    private static void AssertJsonEquals(string expectedJson, JsonElement actual)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        Assert.True(
            JsonElement.DeepEquals(expected.RootElement, actual),
            $"Expected JSON {expected.RootElement} but received {actual}.");
    }

    private static async Task<string> AssertInvalidApiKeyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = payload.GetProperty("error");
        Assert.Equal("invalid_api_key", error.GetProperty("code").GetString());
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("param").ValueKind);
        return error.ToString();
    }

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

    private static ProjectMcpAssignment OptionalGrant(string serverId, params string[] enabledTools) => new()
    {
        ServerId = serverId,
        Required = false,
        EnabledTools = [.. enabledTools]
    };

    private sealed record Invocation(string Mode, string[] Arguments, int ProcessId);
}
