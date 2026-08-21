using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Errors;
using CodexGateway.Models;

namespace CodexGateway.Tests;

public sealed class McpMetadataDiscoveryTests
{
    [Fact]
    public void Page_parser_preserves_server_and_tool_metadata_and_returns_cursor()
    {
        var resolved = CreateResolved("alpha-server", required: true);
        var expected = McpMetadataDiscoveryService.BuildExpectedServers([resolved]);
        var discovered = new Dictionary<string, DiscoveredMcpServer>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(
            """
            {
              "data": [
                {
                  "name": "alpha_server",
                  "serverInfo": {
                    "name": "runtime-alpha",
                    "version": "1.2.3",
                    "title": "Alpha",
                    "description": "Server description",
                    "websiteUrl": "https://mcp.example.test",
                    "icons": [{ "src": "data:image/svg+xml;base64,PHN2Zy8+", "extension": 7 }]
                  },
                  "tools": {
                    "lookup": {
                      "name": "lookup",
                      "title": "Lookup",
                      "description": "Looks up a record.",
                      "inputSchema": {
                        "type": "object",
                        "properties": { "id": { "type": ["string", "integer"] } },
                        "required": ["id"]
                      },
                      "outputSchema": { "type": "object" },
                      "annotations": { "readOnlyHint": true, "extension": { "level": 2 } },
                      "icons": [{ "src": "https://mcp.example.test/tool.svg" }],
                      "_meta": { "vendor.example/template": "record" }
                    }
                  }
                }
              ],
              "nextCursor": "page-2"
            }
            """);

        McpMetadataDiscoveryService.ParsePage(
            document.RootElement,
            expected,
            discovered,
            out var nextCursor);

        Assert.Equal("page-2", nextCursor);
        var server = Assert.Single(discovered).Value;
        Assert.Equal("alpha-server", server.ServerId);
        Assert.NotNull(server.ServerInfo);
        Assert.Equal("runtime-alpha", server.ServerInfo.Name);
        Assert.Equal("Server description", server.ServerInfo.Description);
        Assert.True(server.ServerInfo.Icons.HasValue);
        Assert.Equal(7, server.ServerInfo.Icons.Value[0].GetProperty("extension").GetInt32());
        var tool = Assert.Single(server.Tools);
        Assert.Equal("lookup", tool.Name);
        Assert.Equal("Looks up a record.", tool.Description);
        Assert.Equal(
            JsonValueKind.Array,
            tool.InputSchema.GetProperty("properties").GetProperty("id").GetProperty("type").ValueKind);
        Assert.True(tool.Annotations.HasValue);
        Assert.True(tool.Annotations.Value.GetProperty("readOnlyHint").GetBoolean());
        Assert.True(tool.Meta.HasValue);
        Assert.Equal(
            "record",
            tool.Meta.Value.GetProperty("vendor.example/template").GetString());
    }

    [Fact]
    public void Page_parser_rejects_non_object_and_duplicate_server_statuses()
    {
        var resolved = CreateResolved("alpha", required: false);
        var expected = McpMetadataDiscoveryService.BuildExpectedServers([resolved]);
        var discovered = new Dictionary<string, DiscoveredMcpServer>(StringComparer.Ordinal);
        using var nonObject = JsonDocument.Parse("[]");
        Assert.Throws<CodexUnavailableException>(() =>
            McpMetadataDiscoveryService.ParsePage(
                nonObject.RootElement,
                expected,
                discovered,
                out _));

        using var page = JsonDocument.Parse(
            """
            {
              "data": [
                {
                  "name": "alpha",
                  "serverInfo": null,
                  "tools": {}
                }
              ]
            }
            """);
        McpMetadataDiscoveryService.ParsePage(page.RootElement, expected, discovered, out _);
        Assert.Throws<CodexUnavailableException>(() =>
            McpMetadataDiscoveryService.ParsePage(page.RootElement, expected, discovered, out _));
    }

    [Fact]
    public void Required_server_must_be_present_and_initialized_but_optional_server_may_be_absent()
    {
        var required = CreateResolved("required", required: true);
        var optional = CreateResolved("optional", required: false);

        Assert.Throws<CodexUnavailableException>(() =>
            McpMetadataDiscoveryService.EnsureRequiredServersAvailable(
                [required, optional],
                new Dictionary<string, DiscoveredMcpServer>(StringComparer.Ordinal)));

        var discovered = new Dictionary<string, DiscoveredMcpServer>(StringComparer.Ordinal)
        {
            ["required"] = new(
                "required",
                new McpServerInfoMetadata("required", "1.0", null, null, null, null),
                [])
        };
        McpMetadataDiscoveryService.EnsureRequiredServersAvailable([required, optional], discovered);
    }

    [Fact]
    public async Task Bounded_reader_counts_all_lines_and_rejects_invalid_utf8()
    {
        await using var boundedStream = new MemoryStream(Encoding.UTF8.GetBytes("{}\n{}\n"));
        var boundedReader = new McpMetadataDiscoveryService.BoundedJsonLineReader(boundedStream, 3);
        Assert.Equal("{}", await boundedReader.ReadLineAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await boundedReader.ReadLineAsync(CancellationToken.None));

        await using var invalidStream = new MemoryStream([0xff, (byte)'\n']);
        var invalidReader = new McpMetadataDiscoveryService.BoundedJsonLineReader(invalidStream, 16);
        await Assert.ThrowsAsync<DecoderFallbackException>(async () =>
            await invalidReader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public void Forwarded_secrets_are_derived_only_from_selected_server_environment()
    {
        var resolved = CreateResolved("alpha", required: false) with
        {
            Definition = (HttpMcpServerDefinition)CreateResolved("alpha", required: false).Definition with
            {
                EnvironmentHeaders = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = "ALPHA_TOKEN"
                },
                EnvironmentVariables = ["ALPHA_EXTRA"]
            }
        };
        var startInfo = new ProcessStartInfo();
        startInfo.Environment.Clear();
        startInfo.Environment["ALPHA_TOKEN"] = "token-value";
        startInfo.Environment["ALPHA_EXTRA"] = "extra-value";
        startInfo.Environment["UNSELECTED_SECRET"] = "not-selected";

        var secrets = McpMetadataDiscoveryService.GetForwardedSecrets(startInfo, [resolved], new Dictionary<string, GatewayMcpRunnerConnection>());

        Assert.Equal(2, secrets.Count);
        Assert.Contains(secrets, secret =>
            secret.EnvironmentVariable == "ALPHA_TOKEN" && secret.Value == "token-value");
        Assert.Contains(secrets, secret =>
            secret.EnvironmentVariable == "ALPHA_EXTRA" && secret.Value == "extra-value");
        Assert.DoesNotContain(secrets, secret => secret.EnvironmentVariable == "UNSELECTED_SECRET");
    }


    [Fact]
    public void Gateway_mode_forwarding_uses_scoped_connection_values_only()
    {
        var resolved = CreateResolved("gateway", required: false) with
        {
            Definition = (HttpMcpServerDefinition)CreateResolved("gateway", required: false).Definition with
            {
                ExecutionMode = McpExecutionMode.Gateway,
                EnvironmentHeaders = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = "UPSTREAM_TOKEN"
                },
                EnvironmentVariables = ["UPSTREAM_EXTRA"]
            }
        };
        var startInfo = new ProcessStartInfo();
        startInfo.Environment.Clear();
        startInfo.Environment["GATEWAY_TOKEN"] = "scoped-token";
        startInfo.Environment["UPSTREAM_TOKEN"] = "upstream-token";
        startInfo.Environment["UPSTREAM_EXTRA"] = "upstream-extra";

        var secrets = McpMetadataDiscoveryService.GetForwardedSecrets(
            startInfo,
            [resolved],
            new Dictionary<string, GatewayMcpRunnerConnection>
            {
                ["gateway"] = new("http://gateway.local/mcp/gateway", "GATEWAY_TOKEN", "scoped-token")
            });

        Assert.Single(secrets);
        Assert.Equal("GATEWAY_TOKEN", secrets[0].EnvironmentVariable);
        Assert.Equal("scoped-token", secrets[0].Value);
    }

    [Fact]
    public void Short_secrets_require_exact_equality_to_avoid_substring_false_positives()
    {
        var secret = new ForwardedMcpSecret("TOKEN", "tiny");
        McpMetadataDiscoveryService.EnsureMetadataDoesNotExposeSecrets(
            [CreateMetadata(description: "A tiny example remains safe.")],
            [secret]);

        var exception = Assert.Throws<CodexUnavailableException>(() =>
            McpMetadataDiscoveryService.EnsureMetadataDoesNotExposeSecrets(
                [CreateMetadata(description: "tiny")],
                [secret]));
        Assert.Equal("MCP metadata discovery could not be completed safely.", exception.Message);
        Assert.DoesNotContain(secret.Value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EncodedSecretCandidates))]
    public void Long_literal_and_obviously_encoded_secrets_fail_closed(string candidate)
    {
        const string secretValue = "sensitive-value-123\u2713";
        var exception = Assert.Throws<CodexUnavailableException>(() =>
            McpMetadataDiscoveryService.EnsureMetadataDoesNotExposeSecrets(
                [CreateMetadata(description: "prefix " + candidate + " suffix")],
                [new ForwardedMcpSecret("TOKEN", secretValue)]));

        Assert.Equal("MCP metadata discovery could not be completed safely.", exception.Message);
        Assert.DoesNotContain(secretValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(candidate, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_property_names_strings_and_numbers_are_scanned_without_mutating_safe_metadata()
    {
        const string numericSecret = "123456789012";
        var unsafeMetadata = CreateMetadata(
            inputSchema: Element("""
                {
                  "outer": {
                    "123456789012": "safe"
                  }
                }
                """));
        Assert.Throws<CodexUnavailableException>(() =>
            McpMetadataDiscoveryService.EnsureMetadataDoesNotExposeSecrets(
                [unsafeMetadata],
                [new ForwardedMcpSecret("TOKEN", numericSecret)]));

        var numericMetadata = CreateMetadata(inputSchema: Element("""{"const":123456789012}"""));
        Assert.Throws<CodexUnavailableException>(() =>
            McpMetadataDiscoveryService.EnsureMetadataDoesNotExposeSecrets(
                [numericMetadata],
                [new ForwardedMcpSecret("TOKEN", numericSecret)]));

        var safeSchema = Element("""
            {
              "type": "object",
              "properties": {
                "value": { "type": ["string", "number"], "x-order": [2, 1] }
              }
            }
            """);
        var safeMetadata = CreateMetadata(inputSchema: safeSchema);
        var before = safeMetadata.Tools[0].InputSchema.GetRawText();
        McpMetadataDiscoveryService.EnsureMetadataDoesNotExposeSecrets(
            [safeMetadata],
            [new ForwardedMcpSecret("TOKEN", numericSecret)]);
        Assert.Equal(before, safeMetadata.Tools[0].InputSchema.GetRawText());
    }

    public static IEnumerable<object[]> EncodedSecretCandidates()
    {
        const string value = "sensitive-value-123\u2713";
        var bytes = Encoding.UTF8.GetBytes(value);
        var base64 = Convert.ToBase64String(bytes);
        var base64Url = base64.Replace('+', '-').Replace('/', '_');
        var hexadecimal = Convert.ToHexString(bytes);
        var componentEncoded = Uri.EscapeDataString(value);
        var fullyEncoded = string.Concat(bytes.Select(valueByte => $"%{valueByte:X2}"));
        return
        [
            [value],
            [base64],
            [base64.TrimEnd('=')],
            [base64Url],
            [base64Url.TrimEnd('=')],
            [hexadecimal],
            [hexadecimal.ToLowerInvariant()],
            [componentEncoded],
            [fullyEncoded],
            [fullyEncoded.ToLowerInvariant()]
        ];
    }

    private static DiscoveredMcpServer CreateMetadata(
        string description = "safe description",
        JsonElement? inputSchema = null) =>
        new(
            "alpha",
            new McpServerInfoMetadata(
                "runtime-alpha",
                "1.0.0",
                "Alpha",
                description,
                "https://mcp.example.test",
                Element("""[{"src":"https://mcp.example.test/icon.svg"}]""")),
            [
                new McpToolMetadata(
                    "lookup",
                    "Lookup",
                    "Looks up a record.",
                    inputSchema ?? Element("""{"type":"object"}"""),
                    Element("""{"type":"object"}"""),
                    Element("""{"readOnlyHint":true}"""),
                    Element("""[{"src":"https://mcp.example.test/tool.svg"}]"""),
                    Element("""{"vendor.example/template":"record"}"""))
            ]);

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ResolvedMcpServer CreateResolved(string id, bool required) => new(
        new HttpMcpServerDefinition
        {
            Id = id,
            Name = id,
            Url = "https://mcp.example.test",
            AvailableTools = ["lookup"]
        },
        ["lookup"],
        required);
}
