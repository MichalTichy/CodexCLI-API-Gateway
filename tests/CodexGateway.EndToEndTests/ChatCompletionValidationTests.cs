using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests;

public sealed class ChatCompletionValidationTests : IDisposable
{
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;

    public ChatCompletionValidationTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
    }

    [Fact]
    public async Task Optional_tool_fields_and_message_name_accept_the_supported_no_tool_shapes()
    {
        var missingTools = ValidRequest();

        var nullTools = ValidRequest();
        nullTools.Add("tools", null);

        var emptyTools = ValidRequest();
        emptyTools["tools"] = new JsonArray();

        var supportedOptions = ValidRequest();
        supportedOptions["tool_choice"] = "none";
        supportedOptions["parallel_tool_calls"] = false;
        supportedOptions["functions"] = new JsonArray();
        supportedOptions["function_call"] = "none";
        supportedOptions["response_format"] = new JsonObject { ["type"] = "text" };
        supportedOptions["messages"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["name"] = "planner-agent_1",
                ["content"] = "hello"
            }
        };

        foreach (var request in new[] { missingTools, nullTools, emptyTools, supportedOptions })
        {
            using var response = await PostChatAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Unsupported_tool_and_search_features_return_openai_shaped_errors_with_the_offending_parameter()
    {
        var tools = ValidRequest();
        tools["tools"] = JsonNode.Parse("""
            [{"type":"function","function":{"name":"lookup"}}]
            """);

        var toolChoice = ValidRequest();
        toolChoice["tool_choice"] = JsonNode.Parse("""
            {"type":"function","function":{"name":"lookup"}}
            """);

        var legacyFunctions = ValidRequest();
        legacyFunctions["functions"] = JsonNode.Parse("""
            [{"name":"lookup","parameters":{"type":"object"}}]
            """);

        var legacyFunctionCall = ValidRequest();
        legacyFunctionCall["function_call"] = "auto";

        var parallel = ValidRequest();
        parallel["parallel_tool_calls"] = true;

        var webSearch = ValidRequest();
        webSearch["web_search_options"] = new JsonObject();

        var toolMessage = ValidRequest();
        toolMessage["messages"] = JsonNode.Parse("""
            [{"role":"tool","tool_call_id":"call_1","content":"result"}]
            """);

        var assistantToolCalls = ValidRequest();
        assistantToolCalls["messages"] = JsonNode.Parse("""
            [{
              "role":"assistant",
              "content":"calling",
              "tool_calls":[{
                "id":"call_1",
                "type":"function",
                "function":{"name":"lookup","arguments":"{}"}
              }]
            }]
            """);

        var assistantFunctionCall = ValidRequest();
        assistantFunctionCall["messages"] = JsonNode.Parse("""
            [{
              "role":"assistant",
              "content":"calling",
              "function_call":{"name":"lookup","arguments":"{}"}
            }]
            """);

        InvalidCase[] cases =
        [
            new(tools, "unsupported_parameter", "tools"),
            new(toolChoice, "unsupported_parameter", "tool_choice"),
            new(legacyFunctions, "unsupported_parameter", "functions"),
            new(legacyFunctionCall, "unsupported_parameter", "function_call"),
            new(parallel, "unsupported_parameter", "parallel_tool_calls"),
            new(webSearch, "unsupported_parameter", "web_search_options"),
            new(toolMessage, "unsupported_parameter", "messages.role"),
            new(assistantToolCalls, "unsupported_parameter", "messages.tool_calls"),
            new(assistantFunctionCall, "unsupported_parameter", "messages.function_call")
        ];

        foreach (var testCase in cases)
        {
            await AssertBadRequestAsync(testCase);
        }
    }

    [Fact]
    public async Task Unsupported_and_invalid_response_formats_return_precise_openai_errors()
    {
        var jsonObject = ValidRequest();
        jsonObject["response_format"] = new JsonObject { ["type"] = "json_object" };

        var unknown = ValidRequest();
        unknown["response_format"] = new JsonObject { ["type"] = "future_format" };

        var nonStrict = JsonSchemaRequest(ValidSchema(), strict: false);
        var streaming = JsonSchemaRequest(ValidSchema());
        streaming["stream"] = true;

        var invalidName = JsonSchemaRequest(ValidSchema(), name: "not a valid name");
        var nonObject = JsonSchemaRequest(new JsonArray());

        var oversized = JsonSchemaRequest(new JsonObject
        {
            ["type"] = "object",
            ["description"] = new string('x', 65 * 1024)
        });

        var tooDeep = JsonSchemaRequest(JsonNode.Parse(DeepSchemaJson(34))!);

        var excessiveProperties = new JsonObject();
        for (var index = 0; index < 1001; index++)
        {
            excessiveProperties[$"p{index}"] = true;
        }

        var tooManyProperties = JsonSchemaRequest(excessiveProperties);
        var longString = JsonSchemaRequest(new JsonObject
        {
            ["description"] = new string('x', (16 * 1024) + 1)
        });
        var remoteReference = JsonSchemaRequest(new JsonObject
        {
            ["$ref"] = "https://schemas.example.test/task.json"
        });

        InvalidCase[] cases =
        [
            new(jsonObject, "unsupported_parameter", "response_format.type"),
            new(unknown, "unsupported_parameter", "response_format.type"),
            new(nonStrict, "unsupported_parameter", "response_format.json_schema.strict"),
            new(streaming, "unsupported_parameter", "response_format"),
            new(invalidName, "invalid_request", "response_format.json_schema.name"),
            new(nonObject, "invalid_request", "response_format.json_schema.schema"),
            new(oversized, "invalid_json_schema", "response_format.json_schema.schema"),
            new(tooDeep, "invalid_json_schema", "response_format.json_schema.schema"),
            new(tooManyProperties, "invalid_json_schema", "response_format.json_schema.schema"),
            new(longString, "invalid_json_schema", "response_format.json_schema.schema"),
            new(remoteReference, "unsupported_json_schema", "response_format.json_schema.schema")
        ];

        foreach (var testCase in cases)
        {
            await AssertBadRequestAsync(testCase);
        }
    }

    [Fact]
    public async Task Valid_json_schema_succeeds_and_invalid_structured_model_output_is_a_sanitized_bad_gateway()
    {
        using var validResponse = await PostChatAsync(JsonSchemaRequest(ValidSchema()));
        validResponse.EnsureSuccessStatusCode();
        var validEnvelope = await validResponse.Content.ReadFromJsonAsync<JsonElement>();
        var structuredContent = validEnvelope.GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        using var structuredDocument = JsonDocument.Parse(Assert.IsType<string>(structuredContent));
        Assert.Equal("ok", structuredDocument.RootElement.GetProperty("result").GetString());

        using var invalidResponse = await PostChatAsync(
            JsonSchemaRequest(ValidSchema(), content: "[scenario:invalid-structured-output]"));
        Assert.Equal(HttpStatusCode.BadGateway, invalidResponse.StatusCode);
        var invalidEnvelope = await invalidResponse.Content.ReadFromJsonAsync<JsonElement>();
        var error = invalidEnvelope.GetProperty("error");
        Assert.Equal("codex_failed", error.GetProperty("code").GetString());
        Assert.Equal("server_error", error.GetProperty("type").GetString());
        Assert.DoesNotContain("This is not JSON", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_usage_option_returns_the_standard_terminal_usage_chunk()
    {
        var request = ValidRequest("stream with usage");
        request["stream"] = true;
        request["stream_options"] = new JsonObject { ["include_usage"] = true };

        using var response = await PostChatAsync(request);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var payloads = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data: {", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line[6..]))
            .ToArray();
        try
        {
            var usageIndex = Array.FindIndex(
                payloads,
                document => document.RootElement.TryGetProperty("usage", out _));
            Assert.True(usageIndex > 0);
            var usageChunk = payloads[usageIndex].RootElement;
            Assert.Empty(usageChunk.GetProperty("choices").EnumerateArray());
            var usage = usageChunk.GetProperty("usage");
            Assert.Equal(10, usage.GetProperty("prompt_tokens").GetInt32());
            Assert.Equal(3, usage.GetProperty("completion_tokens").GetInt32());
            Assert.Equal(13, usage.GetProperty("total_tokens").GetInt32());
            Assert.EndsWith("data: [DONE]\n\n", body, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var payload in payloads)
            {
                payload.Dispose();
            }
        }
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

    private async Task AssertBadRequestAsync(InvalidCase testCase)
    {
        using var response = await PostChatAsync(testCase.Request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = envelope.GetProperty("error");
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.Equal(testCase.Code, error.GetProperty("code").GetString());
        Assert.Equal(testCase.Parameter, error.GetProperty("param").GetString());
    }

    private Task<HttpResponseMessage> PostChatAsync(JsonObject request) =>
        _client.PostAsync(
            "/v1/chat/completions",
            new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));

    private static JsonObject ValidRequest(string content = "hello") => new()
    {
        ["model"] = "gpt-test-sol",
        ["messages"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = content
            }
        }
    };

    private static JsonObject JsonSchemaRequest(
        JsonNode schema,
        bool strict = true,
        string name = "samwise_plan",
        string content = "classify this")
    {
        var request = ValidRequest(content);
        request["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = name,
                ["strict"] = strict,
                ["schema"] = schema
            }
        };
        return request;
    }

    private static JsonObject ValidSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["result"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("result"),
        ["additionalProperties"] = false
    };

    private static string DeepSchemaJson(int levels) =>
        string.Concat(Enumerable.Repeat("{\"nested\":", levels)) + "true" + new string('}', levels);

    private sealed record InvalidCase(JsonObject Request, string Code, string Parameter);
}
