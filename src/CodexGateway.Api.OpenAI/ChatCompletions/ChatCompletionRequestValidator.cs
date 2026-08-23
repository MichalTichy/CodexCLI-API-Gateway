using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexGateway.Logic.Errors;

namespace CodexGateway.Api.OpenAI.ChatCompletions;

public static partial class ChatCompletionRequestValidator
{
    private const int MaximumSchemaBytes = 64 * 1024;
    private const int MaximumSchemaDepth = 32;
    private const int MaximumSchemaProperties = 1000;
    private const int MaximumSchemaNodes = 10000;
    private const int MaximumSchemaStringLength = 16 * 1024;
    private const int MaximumSchemaPropertyNameLength = 256;
    private const int MaximumFormatDescriptionLength = 1024;

    public static ValidatedJsonSchemaResponseFormat? Validate(ChatCompletionRequest request)
    {
        ValidateMessages(request.Messages);
        ValidateRequestTools(request);
        ValidateWebSearch(request.WebSearchOptions);

        return ValidateResponseFormat(request.ResponseFormat, request.Stream);
    }

    private static void ValidateMessages(IReadOnlyList<ChatMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            throw GatewayException.InvalidRequest("At least one message is required.", parameter: "messages");
        }

        foreach (var message in messages)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Role))
            {
                throw GatewayException.InvalidRequest("Every message requires a role.", parameter: "messages.role");
            }

            var role = message.Role.Trim();
            if (role == "tool")
            {
                throw Unsupported("Tool-result messages are not supported by this Gateway endpoint.", "messages.role");
            }

            if (role is not ("system" or "developer" or "user" or "assistant"))
            {
                throw GatewayException.InvalidRequest(
                    $"Message role '{role}' is not supported.",
                    "unsupported_message_role",
                    "messages.role");
            }

            if (message.Name is not null && !MessageNamePattern().IsMatch(message.Name))
            {
                throw GatewayException.InvalidRequest(
                    "Message names must contain 1-64 letters, numbers, underscores, or hyphens.",
                    parameter: "messages.name");
            }

            if (HasNonEmptyArray(message.ToolCalls, "messages.tool_calls"))
            {
                throw Unsupported(
                    "Assistant tool calls are not supported by this Gateway endpoint.",
                    "messages.tool_calls");
            }

            if (HasValue(message.FunctionCall))
            {
                throw Unsupported(
                    "Legacy assistant function calls are not supported by this Gateway endpoint.",
                    "messages.function_call");
            }
        }
    }

    private static void ValidateRequestTools(ChatCompletionRequest request)
    {
        if (HasNonEmptyArray(request.Tools, "tools"))
        {
            throw Unsupported(
                "Request-level OpenAI function tools are not supported by this Gateway endpoint.",
                "tools");
        }

        if (HasNonEmptyArray(request.Functions, "functions"))
        {
            throw Unsupported(
                "Legacy request-level OpenAI functions are not supported by this Gateway endpoint.",
                "functions");
        }

        if (request.ToolChoice is { } toolChoice &&
            toolChoice.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
            !(toolChoice.ValueKind == JsonValueKind.String && toolChoice.GetString() == "none"))
        {
            throw Unsupported(
                "Request-level OpenAI function tool selection is not supported by this Gateway endpoint.",
                "tool_choice");
        }

        if (request.FunctionCall is { } functionCall &&
            functionCall.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
            !(functionCall.ValueKind == JsonValueKind.String && functionCall.GetString() == "none"))
        {
            throw Unsupported(
                "Legacy request-level OpenAI function selection is not supported by this Gateway endpoint.",
                "function_call");
        }

        if (request.ParallelToolCalls == true)
        {
            throw Unsupported(
                "Parallel request-level tool calls are not supported by this Gateway endpoint.",
                "parallel_tool_calls");
        }
    }

    private static void ValidateWebSearch(JsonElement? webSearchOptions)
    {
        if (HasValue(webSearchOptions))
        {
            throw Unsupported(
                "Request-level hosted web search is not supported; use Gateway-managed MCP tools instead.",
                "web_search_options");
        }
    }

    private static ValidatedJsonSchemaResponseFormat? ValidateResponseFormat(
        ChatResponseFormat? responseFormat,
        bool stream)
    {
        if (responseFormat is null)
        {
            return null;
        }

        if (responseFormat.Type == "text")
        {
            if (responseFormat.JsonSchema is not null)
            {
                throw GatewayException.InvalidRequest(
                    "response_format.json_schema is only valid when response_format.type is 'json_schema'.",
                    parameter: "response_format.json_schema");
            }

            return null;
        }

        if (responseFormat.Type != "json_schema")
        {
            throw Unsupported(
                "Only response_format types 'text' and 'json_schema' are supported by this Gateway endpoint.",
                "response_format.type");
        }

        if (stream)
        {
            throw Unsupported(
                "JSON-schema structured output is not supported with streaming in this Gateway version.",
                "response_format");
        }

        var format = responseFormat.JsonSchema ?? throw GatewayException.InvalidRequest(
            "response_format.json_schema is required.",
            parameter: "response_format.json_schema");
        if (string.IsNullOrWhiteSpace(format.Name) || !FormatNamePattern().IsMatch(format.Name))
        {
            throw GatewayException.InvalidRequest(
                "The JSON-schema response format name must contain 1-64 letters, numbers, underscores, or hyphens.",
                parameter: "response_format.json_schema.name");
        }

        if (format.Description?.Length > MaximumFormatDescriptionLength)
        {
            throw GatewayException.InvalidRequest(
                $"The JSON-schema response format description cannot exceed {MaximumFormatDescriptionLength} characters.",
                parameter: "response_format.json_schema.description");
        }

        if (format.Strict == false)
        {
            throw Unsupported(
                "This Gateway requires strict JSON-schema structured output.",
                "response_format.json_schema.strict");
        }

        if (format.Schema.ValueKind != JsonValueKind.Object)
        {
            throw GatewayException.InvalidRequest(
                "response_format.json_schema.schema must be a JSON object.",
                parameter: "response_format.json_schema.schema");
        }

        if (Encoding.UTF8.GetByteCount(format.Schema.GetRawText()) > MaximumSchemaBytes)
        {
            throw InvalidSchema($"The JSON schema cannot exceed {MaximumSchemaBytes} UTF-8 bytes.");
        }

        var limits = new SchemaLimits();
        ValidateSchemaValue(format.Schema, depth: 1, limits);
        return new ValidatedJsonSchemaResponseFormat(
            format.Name,
            format.Description,
            format.Schema.Clone());
    }

    private static void ValidateSchemaValue(JsonElement element, int depth, SchemaLimits limits)
    {
        if (depth > MaximumSchemaDepth || ++limits.Nodes > MaximumSchemaNodes)
        {
            throw InvalidSchema("The JSON schema exceeds the configured nesting or complexity limit.");
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (++limits.Properties > MaximumSchemaProperties ||
                        property.Name.Length > MaximumSchemaPropertyNameLength)
                    {
                        throw InvalidSchema("The JSON schema exceeds the configured property limit.");
                    }

                    if (property.Name is "$dynamicRef" or "$recursiveRef" or "$id")
                    {
                        throw UnsupportedSchema($"Schema keyword '{property.Name}' is not supported.");
                    }

                    if (property.Name == "$ref" &&
                        (property.Value.ValueKind != JsonValueKind.String ||
                         property.Value.GetString() is not { } reference ||
                         !reference.StartsWith('#')))
                    {
                        throw UnsupportedSchema("Remote JSON Schema references are not supported.");
                    }

                    ValidateSchemaValue(property.Value, depth + 1, limits);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateSchemaValue(item, depth + 1, limits);
                }

                break;
            case JsonValueKind.String:
                if (element.GetString()!.Length > MaximumSchemaStringLength)
                {
                    throw InvalidSchema("A JSON schema string exceeds the configured length limit.");
                }

                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;
            default:
                throw InvalidSchema("The JSON schema contains an unsupported JSON value.");
        }
    }

    private static bool HasNonEmptyArray(JsonElement? value, string parameter)
    {
        if (!HasValue(value))
        {
            return false;
        }

        if (value!.Value.ValueKind != JsonValueKind.Array)
        {
            throw GatewayException.InvalidRequest($"{parameter} must be an array or null.", parameter: parameter);
        }

        return value.Value.GetArrayLength() > 0;
    }

    private static bool HasValue(JsonElement? value) =>
        value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };

    private static GatewayException Unsupported(string message, string parameter) =>
        GatewayException.InvalidRequest(message, "unsupported_parameter", parameter);

    private static GatewayException InvalidSchema(string message) =>
        GatewayException.InvalidRequest(message, "invalid_json_schema", "response_format.json_schema.schema");

    private static GatewayException UnsupportedSchema(string message) =>
        GatewayException.InvalidRequest(message, "unsupported_json_schema", "response_format.json_schema.schema");

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatNamePattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageNamePattern();

    private sealed class SchemaLimits
    {
        public int Nodes { get; set; }

        public int Properties { get; set; }
    }
}
