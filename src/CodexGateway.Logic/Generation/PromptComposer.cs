using System.Text;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;

namespace CodexGateway.Logic.Generation;

public sealed class PromptComposer(IFileStore files)
{
    public async Task<CodexPrompt> ComposeAsync(
        AssistantResponseInput input,
        ProjectDefinition? project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateContext(input.Context);

        if (input.Messages is null || input.Messages.Count == 0)
        {
            throw GatewayException.InvalidRequest("At least one message is required.", parameter: "messages");
        }

        var fileIds = new List<string>();
        var uniqueFileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fileId in input.FileIds ?? [])
        {
            AddFileId(fileId, fileIds, uniqueFileIds);
        }

        var conversation = new StringBuilder();
        foreach (var message in input.Messages)
        {
            if (message is null || !Enum.IsDefined(message.Role))
            {
                throw GatewayException.InvalidRequest("Every message requires a supported role.", parameter: "messages.role");
            }

            conversation.Append('[').Append(RoleName(message.Role)).AppendLine("]");
            conversation.AppendLine(ReadContent(message.Content, fileIds, uniqueFileIds));
            conversation.AppendLine();
        }

        var referencedFiles = new List<FileRecord>(fileIds.Count);
        foreach (var fileId in fileIds)
        {
            referencedFiles.Add(await files.GetRequiredAsync(
                project?.Id,
                input.Context.ApiKeyId,
                fileId,
                cancellationToken));
        }

        var prompt = new StringBuilder();
        prompt.AppendLine("You are responding through Codex CLI API Gateway.");
        prompt.AppendLine("The current working directory is an isolated run workspace.");
        prompt.AppendLine("Use ./artifacts for input and output files. Shell-command network access is disabled.");
        if (input.StructuredOutput is { } output)
        {
            prompt.Append("Return only valid JSON matching the requested structured output");
            if (!string.IsNullOrWhiteSpace(output.Name))
            {
                prompt.Append(" '").Append(output.Name.Trim()).Append('\'');
            }

            prompt.AppendLine(".");
            if (!string.IsNullOrWhiteSpace(output.Description))
            {
                prompt.Append("Structured output purpose: ").AppendLine(output.Description.Trim());
            }
        }

        if (project is not null)
        {
            prompt.AppendLine($"This run belongs to project '{project.Id}'. Existing project artifacts are available in ./artifacts and successful changes there will be persisted.");
        }
        else
        {
            prompt.AppendLine("This run has no project. Its workspace and generated artifacts will be deleted after the response.");
        }

        if (referencedFiles.Count > 0)
        {
            prompt.AppendLine("Referenced files:");
            foreach (var file in referencedFiles)
            {
                var runName = project is null ? file.Id + "_" + file.FileName : file.StoredName;
                prompt.Append("- ").Append(file.Id).Append(": ./artifacts/").AppendLine(runName);
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Conversation:");
        prompt.Append(conversation);
        return new CodexPrompt(
            prompt.ToString(),
            project is null ? fileIds : [],
            input.StructuredOutput is { } structuredOutput ? structuredOutput.Schema.Clone() : null);
    }

    private static void ValidateContext(GatewayRequestContext context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ApiKeyId))
        {
            throw new InvalidApiKeyException();
        }
    }

    private static string ReadContent(
        IReadOnlyList<InputContentPart>? parts,
        List<string> fileIds,
        HashSet<string> uniqueFileIds)
    {
        if (parts is null)
        {
            throw GatewayException.InvalidRequest("Message content is required.", parameter: "messages.content");
        }

        var result = new StringBuilder();
        foreach (var part in parts)
        {
            switch (part)
            {
                case TextContentPart text:
                    result.AppendLine(text.Text ?? string.Empty);
                    break;
                case FileContentPart file:
                    AddFileId(file.FileId, fileIds, uniqueFileIds);
                    result.Append("[Attached file: ").Append(file.FileId).AppendLine("]");
                    break;
                case null:
                    throw GatewayException.InvalidRequest("Message content parts cannot be null.", parameter: "messages.content");
                default:
                    throw GatewayException.InvalidRequest("The message contains an unsupported content part.", "unsupported_content_type", "messages.content");
            }
        }

        return result.ToString().TrimEnd();
    }

    private static void AddFileId(
        string? fileId,
        List<string> fileIds,
        HashSet<string> uniqueFileIds)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw GatewayException.InvalidRequest("File IDs cannot be null or empty.", parameter: "files");
        }

        if (uniqueFileIds.Add(fileId))
        {
            fileIds.Add(fileId);
        }
    }

    private static string RoleName(GenerationMessageRole role) => role switch
    {
        GenerationMessageRole.System => "system",
        GenerationMessageRole.Developer => "developer",
        GenerationMessageRole.User => "user",
        GenerationMessageRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };
}
