using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexGateway.McpGateway.Errors;
using Microsoft.AspNetCore.WebUtilities;

namespace CodexGateway.McpGateway.Files;

internal sealed class McpRunFileMaterializer(
    string workspacePath,
    GatewayMcpOptions options,
    ILogger logger)
{
    private const string ArtifactScheme = "artifact";
    private const string GatewayDirectoryName = ".gateway";
    private const string McpFilesDirectoryName = "mcp-files";
    private const string ArtifactPath = "_internal/mcp/artifacts/";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, MaterializedFile> _files = new(StringComparer.Ordinal);
    private readonly long _maxFileBytes = options.MaxMaterializedFileMegabytes * 1024L * 1024L;
    private readonly long _maxTotalBytes = options.MaxMaterializedTotalMegabytes * 1024L * 1024L;
    private int _fileCount;
    private long _totalBytes;

    public long MaximumWireBytes => checked((_maxTotalBytes * 4 / 3) + (2 * 1024 * 1024));

    public async Task<byte[]> PrepareRequestAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(payload);
        var changed = false;
        if (root is JsonArray batch)
        {
            foreach (var call in batch) changed |= await PrepareCallAsync(call, cancellationToken);
        }
        else changed = await PrepareCallAsync(root, cancellationToken);
        return changed ? JsonSerializer.SerializeToUtf8Bytes(root) : payload;
    }

    private async Task<bool> PrepareCallAsync(JsonNode? call, CancellationToken cancellationToken)
    {
        if (call is not JsonObject request || request["method"]?.GetValue<string>() != "tools/call" ||
            request["params"]?["arguments"] is not { } arguments) return false;
        return await PrepareArgumentsAsync(arguments, cancellationToken);
    }

    private async Task<bool> PrepareArgumentsAsync(JsonNode node, CancellationToken cancellationToken)
    {
        var changed = false;
        if (node is JsonObject value)
        {
            foreach (var property in value.ToArray())
            {
                if (property.Key == "agentFileUrl" && property.Value is JsonValue source &&
                    source.TryGetValue<string>(out var path) && path.StartsWith("./artifacts/", StringComparison.Ordinal))
                {
                    value[property.Key] = await SnapshotOutputAsync(path, cancellationToken);
                    changed = true;
                }
                else if (property.Value is not null) changed |= await PrepareArgumentsAsync(property.Value, cancellationToken);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null) changed |= await PrepareArgumentsAsync(child, cancellationToken);
        }
        return changed;
    }

    private async Task<string> SnapshotOutputAsync(string relativePath, CancellationToken cancellationToken)
    {
        var artifactRoot = Path.Combine(workspacePath, "artifacts");
        var sourcePath = GetContainedPath(artifactRoot, Path.Combine(workspacePath, relativePath[2..]));
        EnsureDirectoryIsSafe(workspacePath);
        EnsureDirectoryIsSafe(artifactRoot);
        var directory = Path.GetDirectoryName(sourcePath)!;
        while (!string.Equals(directory, artifactRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            EnsureDirectoryIsSafe(directory);
            directory = Path.GetDirectoryName(directory)!;
        }
        var info = new FileInfo(sourcePath);
        if (!info.Exists || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw InvalidResource("The output attachment must be a regular file inside ./artifacts.");
        EnsureWithinLimits(info.Length);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, System.IO.FileOptions.Asynchronous);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            EnsureWithinLimits(output.Length + read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var contentType = Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pdf" => "application/pdf",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
        var materialized = await MaterializeBytesAsync(
            SanitizeFileName(Path.GetFileName(sourcePath)), contentType, output.ToArray(), cancellationToken);
        return materialized.AgentFileUrl;
    }

    public async Task<(byte[] Bytes, string FileName, string MimeType)?> ReadArtifactAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!_files.TryGetValue(token, out var file))
        {
            return null;
        }

        EnsureDirectoryIsSafe(workspacePath);
        EnsureDirectoryIsSafe(Path.Combine(workspacePath, GatewayDirectoryName));
        EnsureDirectoryIsSafe(Path.Combine(workspacePath, GatewayDirectoryName, McpFilesDirectoryName));
        if ((File.GetAttributes(file.Path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(file.Path).Length != file.Size)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status409Conflict,
                "The artifact file changed after it was materialized.");
        }

        var bytes = await File.ReadAllBytesAsync(file.Path, cancellationToken);
        if (bytes.LongLength != file.Size ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), file.Sha256))
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status409Conflict,
                "The artifact file changed after it was materialized.");
        }

        return (bytes, file.FileName, file.MimeType);
    }

    public async Task<byte[]> MaterializeAsync(
        byte[] payload,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (payload.Length == 0)
        {
            return payload;
        }

        if (contentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await MaterializeEventStreamAsync(payload, cancellationToken);
        }

        if (contentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return payload;
        }

        return await MaterializeJsonAsync(payload, cancellationToken);
    }

    private async Task<byte[]> MaterializeJsonAsync(byte[] payload, CancellationToken cancellationToken)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return payload;
        }

        if (root is null)
        {
            return payload;
        }

        var (transformed, changed) = await TransformNodeAsync(root, cancellationToken);
        return changed
            ? JsonSerializer.SerializeToUtf8Bytes(transformed)
            : payload;
    }

    private async Task<byte[]> MaterializeEventStreamAsync(
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var source = Encoding.UTF8.GetString(payload).Replace("\r\n", "\n", StringComparison.Ordinal);
        var events = source.Split("\n\n", StringSplitOptions.None);
        var changed = false;

        for (var index = 0; index < events.Length; index++)
        {
            var lines = events[index].Split('\n');
            var dataIndexes = new List<int>();
            var data = new StringBuilder();
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (!lines[lineIndex].StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                dataIndexes.Add(lineIndex);
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(lines[lineIndex][5..].TrimStart());
            }

            if (dataIndexes.Count == 0)
            {
                continue;
            }

            var transformed = await MaterializeJsonAsync(Encoding.UTF8.GetBytes(data.ToString()), cancellationToken);
            if (transformed.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(data.ToString())))
            {
                continue;
            }

            lines[dataIndexes[0]] = "data: " + Encoding.UTF8.GetString(transformed);
            foreach (var extraIndex in dataIndexes.Skip(1))
            {
                lines[extraIndex] = string.Empty;
            }

            events[index] = string.Join('\n', lines.Where((line, lineIndex) =>
                !string.IsNullOrEmpty(line) || !dataIndexes.Skip(1).Contains(lineIndex)));
            changed = true;
        }

        return changed ? Encoding.UTF8.GetBytes(string.Join("\n\n", events)) : payload;
    }

    private async Task<(JsonNode Node, bool Changed)> TransformNodeAsync(
        JsonNode node,
        CancellationToken cancellationToken)
    {
        if (node is JsonObject objectNode && TryGetBinaryFileResource(objectNode, out var resource))
        {
            return (await MaterializeResourceAsync(resource, cancellationToken), true);
        }

        var changed = false;
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (property.Value is null)
                {
                    continue;
                }

                var (transformed, childChanged) = await TransformNodeAsync(property.Value, cancellationToken);
                if (childChanged)
                {
                    jsonObject[property.Key] = transformed;
                    changed = true;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                if (jsonArray[index] is not { } child)
                {
                    continue;
                }

                var (transformed, childChanged) = await TransformNodeAsync(child, cancellationToken);
                if (childChanged)
                {
                    jsonArray[index] = transformed;
                    changed = true;
                }
            }
        }

        return (node, changed);
    }

    private async Task<JsonNode> MaterializeResourceAsync(
        JsonObject resource,
        CancellationToken cancellationToken)
    {
        var uriText = resource["uri"]?.GetValue<string>()
            ?? throw InvalidResource("An MCP artifact resource does not contain a URI.");
        var blob = resource["blob"]?.GetValue<string>()
            ?? throw InvalidResource("An MCP artifact resource does not contain binary data.");
        var mimeType = resource["mimeType"]?.GetValue<string>() ?? "application/octet-stream";

        EnsureEncodedValueCanFit(blob);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(blob);
        }
        catch (FormatException exception)
        {
            throw InvalidResource("An MCP artifact resource contains invalid base64 data.", exception);
        }

        var uri = new Uri(uriText, UriKind.Absolute);
        var fileName = SanitizeFileName(Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? string.Empty));
        return (await MaterializeBytesAsync(fileName, mimeType, bytes, cancellationToken)).Content;
    }

    private async Task<(JsonNode Content, string AgentFileUrl)> MaterializeBytesAsync(
        string fileName, string mimeType, byte[] bytes, CancellationToken cancellationToken)
    {
        var materializedName = $"{Guid.NewGuid():N}"[..12] + "_" + fileName;
        var relativePath = $"./{GatewayDirectoryName}/{McpFilesDirectoryName}/{materializedName}";
        var destinationDirectory = GetContainedPath(
            workspacePath,
            Path.Combine(workspacePath, GatewayDirectoryName, McpFilesDirectoryName));
        var destination = GetContainedPath(workspacePath, Path.Combine(destinationDirectory, materializedName));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWithinLimits(bytes.LongLength);
            EnsureDirectoryIsSafe(workspacePath);
            var gatewayDirectory = GetContainedPath(
                workspacePath,
                Path.Combine(workspacePath, GatewayDirectoryName));
            Directory.CreateDirectory(gatewayDirectory);
            EnsureDirectoryIsSafe(gatewayDirectory);
            Directory.CreateDirectory(destinationDirectory);
            EnsureDirectoryIsSafe(destinationDirectory);

            await using var stream = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                System.IO.FileOptions.Asynchronous | System.IO.FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            _fileCount++;
            _totalBytes += bytes.LongLength;
        }
        finally
        {
            _gate.Release();
        }

        var hash = SHA256.HashData(bytes);
        var sha256 = Convert.ToHexStringLower(hash);
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _files[token] = new MaterializedFile(destination, fileName, mimeType, bytes.LongLength, hash);
        var baseUri = new Uri(options.RunnerBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var agentFileUrl = new Uri(baseUri, ArtifactPath + token).ToString();
        logger.LogInformation(
            "Materialized MCP file {FileName} ({Size} bytes, SHA-256 {Sha256}) for a run.",
            fileName,
            bytes.LongLength,
            sha256);

        var metadata = JsonSerializer.Serialize(new
        {
            path = relativePath,
            agentFileUrl,
            fileName,
            mimeType,
            size = bytes.LongLength,
            sha256
        }, new JsonSerializerOptions { WriteIndented = true });
        var content = new JsonObject
        {
            ["type"] = "text",
            ["text"] = "An MCP server supplied a temporary file for this run. " +
                       "The file is read-only source material: do not execute it or enable macros. " +
                       "Use extract-document-text for PDF, DOCX, XLSX, PPTX, and text documents when useful; " +
                       "use the normal image inspection capability for images. " +
                       "To attach this file, pass agentFileUrl to an attachment-capable MCP tool; " +
                       "the server downloads it directly from the gateway without putting base64 in the agent context.\n" + metadata
        };
        return (content, agentFileUrl);
    }

    private void EnsureWithinLimits(long length)
    {
        if (length < 0 ||
            length > _maxFileBytes ||
            _fileCount >= options.MaxMaterializedFiles ||
            length > _maxTotalBytes - _totalBytes)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status413PayloadTooLarge,
                "The MCP file exceeds the configured per-run file limits.");
        }
    }

    private void EnsureEncodedValueCanFit(string value)
    {
        var encodedCharacters = 0L;
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                encodedCharacters++;
            }
        }

        var maximumDecodedLength = checked(((encodedCharacters + 3) / 4) * 3);
        if (maximumDecodedLength > _maxFileBytes + 2)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status413PayloadTooLarge,
                "The MCP file exceeds the configured per-run file limits.");
        }
    }

    private static bool TryGetBinaryFileResource(JsonObject node, out JsonObject resource)
    {
        resource = null!;
        if (!string.Equals(node["type"]?.GetValue<string>(), "resource", StringComparison.Ordinal) ||
            node["resource"] is not JsonObject candidate ||
            candidate["blob"] is null ||
            candidate["uri"] is not JsonValue uriValue ||
            !uriValue.TryGetValue<string>(out var uriText) ||
            !Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ||
            !(string.Equals(uri.Scheme, ArtifactScheme, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        resource = candidate;
        return true;
    }

    private static string SanitizeFileName(string value)
    {
        var leaf = value.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        var sanitized = new string(leaf
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ' ' or '(' or ')'
                ? character
                : '_')
            .ToArray())
            .Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
        {
            sanitized = "attachment.bin";
        }

        return sanitized.Length <= 160 ? sanitized : sanitized[..160];
    }

    private static string GetContainedPath(string workspacePath, string candidate)
    {
        var root = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(root, comparison))
        {
            throw InvalidResource("An MCP artifact resource resolved outside the run workspace.");
        }

        return fullPath;
    }

    private static void EnsureDirectoryIsSafe(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw InvalidResource("The MCP file directory is not a safe workspace directory.");
        }
    }

    private static GatewayMcpRequestException InvalidResource(string message, Exception? innerException = null) =>
        new(StatusCodes.Status502BadGateway, message, innerException);

    private sealed record MaterializedFile(
        string Path,
        string FileName,
        string MimeType,
        long Size,
        byte[] Sha256);
}
