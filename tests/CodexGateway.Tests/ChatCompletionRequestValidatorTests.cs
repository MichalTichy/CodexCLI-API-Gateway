using System.Text.Json;
using CodexGateway.Logic.Errors;
using CodexGateway.Api.OpenAI;
using Microsoft.AspNetCore.Http;

namespace CodexGateway.Tests;

public sealed class ChatCompletionRequestValidatorTests
{
    [Fact]
    public void Missing_null_and_empty_tools_are_accepted()
    {
        foreach (var tools in new JsonElement?[] { null, Element("null"), Element("[]") })
        {
            var request = ValidRequest();
            request.Tools = tools;

            Assert.Null(ChatCompletionRequestValidator.Validate(request));
        }
    }

    [Fact]
    public void Nonempty_request_tools_are_rejected_explicitly()
    {
        var request = ValidRequest();
        request.Tools = Element("""
            [{"type":"function","function":{"name":"lookup"}}]
            """);

        AssertRejected(request, "unsupported_parameter", "tools");
    }

    [Fact]
    public void Legacy_functions_accept_no_function_shapes_and_reject_function_requests()
    {
        foreach (var functions in new JsonElement?[] { null, Element("null"), Element("[]") })
        {
            var accepted = ValidRequest();
            accepted.Functions = functions;
            accepted.FunctionCall = Element("\"none\"");

            Assert.Null(ChatCompletionRequestValidator.Validate(accepted));
        }

        var functionsRequest = ValidRequest();
        functionsRequest.Functions = Element("""
            [{"name":"lookup","parameters":{"type":"object"}}]
            """);
        AssertRejected(functionsRequest, "unsupported_parameter", "functions");

        foreach (var functionCall in new[]
                 {
                     Element("\"auto\""),
                     Element("""{"name":"lookup"}""")
                 })
        {
            var functionCallRequest = ValidRequest();
            functionCallRequest.FunctionCall = functionCall;
            AssertRejected(functionCallRequest, "unsupported_parameter", "function_call");
        }
    }

    [Fact]
    public void Tool_choice_none_and_parallel_false_are_accepted_but_function_choice_and_parallel_true_are_rejected()
    {
        var accepted = ValidRequest();
        accepted.ToolChoice = Element("\"none\"");
        accepted.ParallelToolCalls = false;
        Assert.Null(ChatCompletionRequestValidator.Validate(accepted));

        var functionChoice = ValidRequest();
        functionChoice.ToolChoice = Element("""
            {"type":"function","function":{"name":"lookup"}}
            """);
        AssertRejected(functionChoice, "unsupported_parameter", "tool_choice");

        var parallel = ValidRequest();
        parallel.ParallelToolCalls = true;
        AssertRejected(parallel, "unsupported_parameter", "parallel_tool_calls");
    }

    [Fact]
    public void Streaming_usage_is_accepted_for_official_openai_client_compatibility()
    {
        var request = ValidRequest();
        request.Stream = true;
        request.StreamOptions = new ChatStreamOptions { IncludeUsage = true };

        Assert.Null(ChatCompletionRequestValidator.Validate(request));
    }

    [Fact]
    public void Present_web_search_options_are_rejected_even_when_empty()
    {
        var request = ValidRequest();
        request.WebSearchOptions = Element("{}");

        AssertRejected(request, "unsupported_parameter", "web_search_options");
    }

    [Fact]
    public void Tool_messages_and_assistant_tool_or_function_calls_are_rejected()
    {
        var toolMessage = ValidRequest();
        toolMessage.Messages = [Message("tool")];
        AssertRejected(toolMessage, "unsupported_parameter", "messages.role");

        var toolCalls = ValidRequest();
        toolCalls.Messages =
        [
            new ChatMessage
            {
                Role = "assistant",
                Content = Element("\"calling\""),
                ToolCalls = Element("""
                    [{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{}"}}]
                    """)
            }
        ];
        AssertRejected(toolCalls, "unsupported_parameter", "messages.tool_calls");

        var functionCall = ValidRequest();
        functionCall.Messages =
        [
            new ChatMessage
            {
                Role = "assistant",
                Content = Element("\"calling\""),
                FunctionCall = Element("""
                    {"name":"lookup","arguments":"{}"}
                    """)
            }
        ];
        AssertRejected(functionCall, "unsupported_parameter", "messages.function_call");
    }

    [Fact]
    public void Valid_optional_message_name_is_accepted()
    {
        var request = ValidRequest();
        request.Messages[0].Name = "planner-agent_1";

        Assert.Null(ChatCompletionRequestValidator.Validate(request));
    }

    [Fact]
    public void Text_response_format_is_accepted()
    {
        var request = ValidRequest();
        request.ResponseFormat = new ChatResponseFormat { Type = "text" };

        Assert.Null(ChatCompletionRequestValidator.Validate(request));
    }

    [Fact]
    public void Unsupported_response_format_variants_are_rejected_explicitly()
    {
        foreach (var type in new[] { "json_object", "future_format" })
        {
            var unsupportedType = ValidRequest();
            unsupportedType.ResponseFormat = new ChatResponseFormat { Type = type };
            AssertRejected(unsupportedType, "unsupported_parameter", "response_format.type");
        }

        var nonStrict = ValidRequest();
        nonStrict.ResponseFormat = ValidResponseFormat(strict: false);
        AssertRejected(nonStrict, "unsupported_parameter", "response_format.json_schema.strict");

        var streaming = ValidRequest();
        streaming.Stream = true;
        streaming.ResponseFormat = ValidResponseFormat();
        AssertRejected(streaming, "unsupported_parameter", "response_format");
    }

    [Fact]
    public void Valid_json_schema_is_returned_for_execution()
    {
        var request = ValidRequest();
        request.ResponseFormat = ValidResponseFormat();

        var validated = Assert.IsType<ValidatedJsonSchemaResponseFormat>(
            ChatCompletionRequestValidator.Validate(request));

        Assert.Equal("samwise_plan", validated.Name);
        Assert.Equal("object", validated.Schema.GetProperty("type").GetString());
        Assert.Equal("string", validated.Schema.GetProperty("properties").GetProperty("result").GetProperty("type").GetString());
    }

    [Fact]
    public void Invalid_format_name_and_nonobject_schema_are_rejected()
    {
        var invalidName = ValidRequest();
        invalidName.ResponseFormat = ValidResponseFormat(name: "not a valid name");
        AssertRejected(invalidName, "invalid_request", "response_format.json_schema.name");

        var nonObject = ValidRequest();
        nonObject.ResponseFormat = ValidResponseFormat(schema: Element("[]"));
        AssertRejected(nonObject, "invalid_request", "response_format.json_schema.schema");
    }

    [Fact]
    public void Oversized_too_deep_and_over_property_limit_schemas_are_rejected()
    {
        var oversized = ValidRequest();
        oversized.ResponseFormat = ValidResponseFormat(
            schema: Element(JsonSerializer.Serialize(new { type = "object", description = new string('x', 65 * 1024) })));
        AssertRejected(oversized, "invalid_json_schema", "response_format.json_schema.schema");

        var tooDeep = ValidRequest();
        tooDeep.ResponseFormat = ValidResponseFormat(schema: Element(DeepSchemaJson(34)));
        AssertRejected(tooDeep, "invalid_json_schema", "response_format.json_schema.schema");

        var properties = Enumerable.Range(0, 1001)
            .Select(index => $"\"p{index}\":true");
        var tooManyProperties = ValidRequest();
        tooManyProperties.ResponseFormat = ValidResponseFormat(schema: Element("{" + string.Join(',', properties) + "}"));
        AssertRejected(tooManyProperties, "invalid_json_schema", "response_format.json_schema.schema");
    }

    [Fact]
    public void Excessive_schema_strings_and_remote_references_are_rejected()
    {
        var longString = ValidRequest();
        longString.ResponseFormat = ValidResponseFormat(
            schema: Element(JsonSerializer.Serialize(new { description = new string('x', (16 * 1024) + 1) })));
        AssertRejected(longString, "invalid_json_schema", "response_format.json_schema.schema");

        var remoteReference = ValidRequest();
        remoteReference.ResponseFormat = ValidResponseFormat(schema: Element("""
            {"$ref":"https://schemas.example.test/task.json"}
            """));
        AssertRejected(remoteReference, "unsupported_json_schema", "response_format.json_schema.schema");
    }

    private static ChatCompletionRequest ValidRequest() => new()
    {
        Model = "gpt-test-sol",
        Messages = [Message("user")]
    };

    private static ChatMessage Message(string role) => new()
    {
        Role = role,
        Content = Element("\"hello\"")
    };

    private static ChatResponseFormat ValidResponseFormat(
        bool? strict = true,
        string name = "samwise_plan",
        JsonElement? schema = null) => new()
        {
            Type = "json_schema",
            JsonSchema = new ChatJsonSchemaFormat
            {
                Name = name,
                Strict = strict,
                Schema = schema ?? Element("""
                    {
                      "type":"object",
                      "properties":{"result":{"type":"string"}},
                      "required":["result"],
                      "additionalProperties":false
                    }
                    """)
            }
        };

    private static void AssertRejected(ChatCompletionRequest request, string code, string parameter)
    {
        var exception = Assert.Throws<GatewayException>(() => ChatCompletionRequestValidator.Validate(request));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(GatewayErrorCategory.InvalidInput, exception.Category);
        Assert.Equal(code, exception.Code);
        Assert.Equal(parameter, exception.Field);
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string DeepSchemaJson(int levels) =>
        string.Concat(Enumerable.Repeat("{\"nested\":", levels)) + "true" + new string('}', levels);
}
