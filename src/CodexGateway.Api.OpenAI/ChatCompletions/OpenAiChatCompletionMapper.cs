using System.Text.Json;
using CodexGateway.Logic;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Generation;

namespace CodexGateway.Api.OpenAI;

public sealed class OpenAiChatCompletionMapper
{
    public AssistantResponseInput ToAssistantResponseInput(
        ChatCompletionRequest request,
        GatewayRequestContext context,
        ValidatedJsonSchemaResponseFormat? responseFormat)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var fileIds = (request.FileIds ?? []).ToArray();
        if (fileIds.Any(string.IsNullOrWhiteSpace))
        {
            throw GatewayException.InvalidRequest("File IDs cannot be null or empty.", parameter: "file_ids");
        }

        var messages = (request.Messages ?? [])
            .Select(MapMessage)
            .ToArray();
        var structuredOutput = responseFormat is null
            ? null
            : new StructuredOutput(
                responseFormat.Name,
                responseFormat.Description,
                responseFormat.Schema.Clone());

        return new AssistantResponseInput(
            context,
            request.Model,
            request.ReasoningEffort,
            messages,
            fileIds,
            structuredOutput);
    }

    private static InputMessage MapMessage(ChatMessage message) =>
        new(MapRole(message.Role), MapContent(message.Content));

    private static GenerationMessageRole MapRole(string role) => role.Trim() switch
    {
        "system" => GenerationMessageRole.System,
        "developer" => GenerationMessageRole.Developer,
        "user" => GenerationMessageRole.User,
        "assistant" => GenerationMessageRole.Assistant,
        _ => throw GatewayException.InvalidRequest(
            $"Message role '{role.Trim()}' is not supported.",
            "unsupported_message_role",
            "messages.role")
    };

    private static IReadOnlyList<InputContentPart> MapContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return [new TextContentPart(content.GetString() ?? string.Empty)];
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            throw GatewayException.InvalidRequest(
                "Message content must be a string or an array of content parts.",
                parameter: "messages.content");
        }

        var result = new List<InputContentPart>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                throw GatewayException.InvalidRequest("Invalid message content part.", parameter: "messages.content");
            }

            if (!part.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                throw GatewayException.InvalidRequest(
                    "Message content parts require a string type.",
                    parameter: "messages.content.type");
            }

            var type = typeElement.GetString();
            switch (type)
            {
                case "text":
                case "input_text":
                    if (!part.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                    {
                        throw GatewayException.InvalidRequest(
                            "Text content parts require a string text value.",
                            parameter: "messages.content.text");
                    }

                    result.Add(new TextContentPart(text.GetString() ?? string.Empty));
                    break;
                case "file":
                case "input_file":
                    var fileId = ReadFileId(part);
                    if (string.IsNullOrWhiteSpace(fileId))
                    {
                        throw GatewayException.InvalidRequest(
                            "File content parts require file_id.",
                            parameter: "messages.content.file_id");
                    }

                    result.Add(new FileContentPart(fileId));
                    break;
                default:
                    throw GatewayException.InvalidRequest(
                        $"Unsupported message content type '{type}'.",
                        "unsupported_content_type",
                        "messages.content.type");
            }
        }

        return result;
    }

    private static string? ReadFileId(JsonElement part)
    {
        if (part.TryGetProperty("file", out var file))
        {
            if (file.ValueKind != JsonValueKind.Object ||
                !file.TryGetProperty("file_id", out var nestedId) ||
                nestedId.ValueKind != JsonValueKind.String)
            {
                throw GatewayException.InvalidRequest(
                    "File content parts require file.file_id to be a string.",
                    parameter: "messages.content.file.file_id");
            }

            return nestedId.GetString();
        }

        if (!part.TryGetProperty("file_id", out var id))
        {
            return null;
        }

        if (id.ValueKind != JsonValueKind.String)
        {
            throw GatewayException.InvalidRequest(
                "File content parts require file_id to be a string.",
                parameter: "messages.content.file_id");
        }

        return id.GetString();
    }
}
