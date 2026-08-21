using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Codex;

public sealed class McpMetadataDiscoveryService(
    ContainerRuntime runtime,
    IWorkspaceManager workspaces,
    IOptions<CodexOptions> options,
    IGatewayMcpSessionFactory gatewayMcpSessionFactory) : IMcpMetadataDiscoveryService
{
    private const int PageSize = 100;
    private const int MaximumPages = 100;
    private const int MaximumOutputBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(options.Value.AppServerRequestTimeoutSeconds);

    public async Task<IReadOnlyList<DiscoveredMcpServer>> DiscoverAsync(
        IReadOnlyList<ResolvedMcpServer> servers,
        CancellationToken cancellationToken)
    {
        if (servers.Count == 0)
        {
            return [];
        }

        var expectedServers = BuildExpectedServers(servers);
        var workspace = await workspaces.CreateEmptyAsync(cancellationToken);
        await using var gatewayLease = await gatewayMcpSessionFactory.CreateAsync(workspace.RootPath, servers, cancellationToken);
        try
        {
            return await DiscoverInContainerAsync(workspace, servers, expectedServers, gatewayLease, cancellationToken);
        }
        finally
        {
            workspaces.Delete(workspace);
        }
    }

    private async Task<IReadOnlyList<DiscoveredMcpServer>> DiscoverInContainerAsync(
        RunWorkspace workspace,
        IReadOnlyList<ResolvedMcpServer> servers,
        IReadOnlyDictionary<string, ResolvedMcpServer> expectedServers,
        GatewayMcpSessionLease gatewayLease,
        CancellationToken cancellationToken)
    {
        var containerName = ContainerRuntime.CreateContainerName();
        var startInfo = runtime.CreateMcpDiscoveryStartInfo(workspace, servers, containerName, gatewayLease.Connections);
        var forwardedSecrets = GetForwardedSecrets(startInfo, servers, gatewayLease.Connections);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var handle = new ContainerRunHandle(runtime, process, containerName);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Process did not start.");
            }
        }
        catch (Exception exception) when (exception is not GatewayException)
        {
            await handle.TerminateAsync();
            throw new CodexUnavailableException("The MCP metadata discovery container could not be started.");
        }

        var stderrTask = ReadAndDrainLimitedAsync(process.StandardError, 64 * 1024);
        using var timeoutCancellation = new CancellationTokenSource(_timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        var reader = new BoundedJsonLineReader(process.StandardOutput.BaseStream, MaximumOutputBytes);
        try
        {
            long requestId = 0;
            await SendRequestAsync(
                process,
                reader,
                ++requestId,
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "codex-cli-api-gateway-mcp-discovery",
                        title = "Codex CLI API Gateway MCP Discovery",
                        version = typeof(McpMetadataDiscoveryService).Assembly.GetName().Version?.ToString() ?? "0.1.0"
                    }
                },
                handle,
                linkedCancellation.Token);
            await WriteMessageAsync(
                process,
                new { method = "initialized", @params = new { } },
                linkedCancellation.Token);

            var discovered = new Dictionary<string, DiscoveredMcpServer>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            for (var page = 0; page < MaximumPages; page++)
            {
                var result = await SendRequestAsync(
                    process,
                    reader,
                    ++requestId,
                    "mcpServerStatus/list",
                    new { cursor, limit = PageSize, detail = "toolsAndAuthOnly" },
                    handle,
                    linkedCancellation.Token);
                ParsePage(result, expectedServers, discovered, out var nextCursor);
                if (nextCursor is null)
                {
                    EnsureRequiredServersAvailable(servers, discovered);
                    var response = servers
                        .Select(server => discovered.GetValueOrDefault(ConfigServerName(server.Definition.Id)))
                        .Where(server => server is not null)
                        .Cast<DiscoveredMcpServer>()
                        .ToArray();
                    EnsureMetadataDoesNotExposeSecrets(response, forwardedSecrets);
                    if (!await handle.TerminateAsync())
                    {
                        throw new CodexUnavailableException(
                            "The MCP metadata discovery container could not be confirmed as stopped.");
                    }

                    await ObserveAfterTerminationAsync(stderrTask);
                    return response;
                }

                if (string.IsNullOrWhiteSpace(nextCursor) || !cursors.Add(nextCursor))
                {
                    throw InvalidProtocol();
                }

                cursor = nextCursor;
            }

            throw InvalidProtocol();
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CodexUnavailableException("MCP metadata discovery timed out.");
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or IOException or DecoderFallbackException)
        {
            throw InvalidProtocol();
        }
        finally
        {
            await handle.TerminateAsync();
            await ObserveAfterTerminationAsync(stderrTask);
        }
    }

    internal static IReadOnlyList<ForwardedMcpSecret> GetForwardedSecrets(
        ProcessStartInfo startInfo,
        IReadOnlyList<ResolvedMcpServer> servers,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection> gatewayConnections)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var selectedNames = servers.SelectMany(server =>
        {
            IEnumerable<string?> names;
            if (server.Definition.ExecutionMode == CodexGateway.Models.McpExecutionMode.Gateway &&
                gatewayConnections.TryGetValue(server.Definition.Id, out var connection))
            {
                names = [connection.BearerTokenEnvironmentVariable];
            }
            else
            {
                names = server.Definition switch
                {
                    HttpMcpServerDefinition http => server.Definition.EnvironmentVariables
                        .Cast<string?>()
                        .Concat(Enumerable.Repeat(http.BearerTokenEnvironmentVariable, 1)),
                    StdioMcpServerDefinition => server.Definition.EnvironmentVariables,
                    _ => []
                };
            }

            return names;
        })
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Cast<string>()
        .ToHashSet(comparer);

        return startInfo.Environment
            .Where(entry =>
                selectedNames.Contains(entry.Key) &&
                !string.IsNullOrEmpty(entry.Value))
            .Select(entry => new ForwardedMcpSecret(entry.Key, entry.Value!))
            .OrderBy(secret => secret.EnvironmentVariable, comparer)
            .ToArray();
    }

    internal static void EnsureMetadataDoesNotExposeSecrets(
        IReadOnlyList<DiscoveredMcpServer> servers,
        IReadOnlyList<ForwardedMcpSecret> secrets)
    {
        if (secrets.Count == 0)
        {
            return;
        }

        var matchers = secrets
            .Select(secret => new SecretMatcher(secret.Value))
            .ToArray();
        foreach (var server in servers)
        {
            if (server.ServerInfo is { } serverInfo)
            {
                EnsureSafe(serverInfo.Name, matchers);
                EnsureSafe(serverInfo.Version, matchers);
                EnsureSafe(serverInfo.Title, matchers);
                EnsureSafe(serverInfo.Description, matchers);
                EnsureSafe(serverInfo.WebsiteUrl, matchers);
                EnsureSafe(serverInfo.Icons, matchers);
            }

            foreach (var tool in server.Tools)
            {
                EnsureSafe(tool.Name, matchers);
                EnsureSafe(tool.Title, matchers);
                EnsureSafe(tool.Description, matchers);
                EnsureSafe(tool.InputSchema, matchers);
                EnsureSafe(tool.OutputSchema, matchers);
                EnsureSafe(tool.Annotations, matchers);
                EnsureSafe(tool.Icons, matchers);
                EnsureSafe(tool.Meta, matchers);
            }
        }
    }

    private static void EnsureSafe(string? candidate, IReadOnlyList<SecretMatcher> matchers)
    {
        if (candidate is null)
        {
            return;
        }

        if (matchers.Any(matcher => matcher.IsMatch(candidate)))
        {
            throw UnsafeMetadata();
        }
    }

    private static void EnsureSafe(JsonElement? metadata, IReadOnlyList<SecretMatcher> matchers)
    {
        if (metadata is not { } element)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    EnsureSafe(property.Name, matchers);
                    EnsureSafe(property.Value, matchers);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    EnsureSafe(item, matchers);
                }

                break;
            case JsonValueKind.String:
                EnsureSafe(element.GetString(), matchers);
                break;
            case JsonValueKind.Number:
                EnsureSafe(element.GetRawText(), matchers);
                break;
        }
    }

    private static CodexUnavailableException UnsafeMetadata() =>
        new("MCP metadata discovery could not be completed safely.");

    private sealed class SecretMatcher
    {
        private const int MinimumSubstringSecretBytes = 12;
        private readonly string _value;
        private readonly string[] _substringPatterns;

        public SecretMatcher(string value)
        {
            _value = value;
            _substringPatterns = Encoding.UTF8.GetByteCount(value) < MinimumSubstringSecretBytes
                ? []
                : BuildSubstringPatterns(value);
        }

        public bool IsMatch(string candidate) =>
            string.Equals(candidate, _value, StringComparison.Ordinal) ||
            _substringPatterns.Any(pattern => candidate.Contains(pattern, StringComparison.Ordinal));

        private static string[] BuildSubstringPatterns(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var base64 = Convert.ToBase64String(bytes);
            var base64Url = base64.Replace('+', '-').Replace('/', '_');
            var hexadecimal = Convert.ToHexString(bytes);
            var componentEncoded = Uri.EscapeDataString(value);
            var fullyEncoded = string.Concat(bytes.Select(valueByte => $"%{valueByte:X2}"));
            return
            [
                .. new[]
                {
                    value,
                    base64,
                    base64.TrimEnd('='),
                    base64Url,
                    base64Url.TrimEnd('='),
                    hexadecimal,
                    hexadecimal.ToLowerInvariant(),
                    componentEncoded,
                    LowerPercentEscapes(componentEncoded),
                    fullyEncoded,
                    fullyEncoded.ToLowerInvariant()
                }
                .Where(pattern => pattern.Length > 0)
                .Distinct(StringComparer.Ordinal)
            ];
        }

        private static string LowerPercentEscapes(string value)
        {
            var characters = value.ToCharArray();
            for (var index = 0; index + 2 < characters.Length; index++)
            {
                if (characters[index] != '%')
                {
                    continue;
                }

                characters[index + 1] = char.ToLowerInvariant(characters[index + 1]);
                characters[index + 2] = char.ToLowerInvariant(characters[index + 2]);
                index += 2;
            }

            return new string(characters);
        }
    }

    internal static IReadOnlyDictionary<string, ResolvedMcpServer> BuildExpectedServers(
        IReadOnlyList<ResolvedMcpServer> servers)
    {
        var expected = new Dictionary<string, ResolvedMcpServer>(StringComparer.Ordinal);
        foreach (var server in servers)
        {
            if (!expected.TryAdd(ConfigServerName(server.Definition.Id), server))
            {
                throw new InvalidOperationException("Resolved MCP server IDs must be unique.");
            }
        }

        return expected;
    }

    internal static void ParsePage(
        JsonElement result,
        IReadOnlyDictionary<string, ResolvedMcpServer> expectedServers,
        IDictionary<string, DiscoveredMcpServer> discovered,
        out string? nextCursor)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProtocol();
        }

        foreach (var status in data.EnumerateArray())
        {
            if (status.ValueKind != JsonValueKind.Object)
            {
                throw InvalidProtocol();
            }

            var statusName = RequiredString(status, "name");
            if (!expectedServers.TryGetValue(statusName, out var expected) ||
                discovered.ContainsKey(statusName))
            {
                throw InvalidProtocol();
            }

            var serverInfo = ParseServerInfo(status);
            var tools = ParseTools(status);
            discovered.Add(
                statusName,
                new DiscoveredMcpServer(expected.Definition.Id, serverInfo, tools));
        }

        if (!result.TryGetProperty("nextCursor", out var cursorElement) ||
            cursorElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            nextCursor = null;
            return;
        }

        if (cursorElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidProtocol();
        }

        nextCursor = cursorElement.GetString();
    }

    private static McpServerInfoMetadata? ParseServerInfo(JsonElement status)
    {
        if (!status.TryGetProperty("serverInfo", out var serverInfo) ||
            serverInfo.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (serverInfo.ValueKind != JsonValueKind.Object)
        {
            throw InvalidProtocol();
        }

        return new McpServerInfoMetadata(
            RequiredString(serverInfo, "name"),
            RequiredString(serverInfo, "version"),
            OptionalString(serverInfo, "title"),
            OptionalString(serverInfo, "description"),
            OptionalString(serverInfo, "websiteUrl"),
            OptionalJson(serverInfo, "icons"));
    }

    private static IReadOnlyList<McpToolMetadata> ParseTools(JsonElement status)
    {
        if (!status.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Object)
        {
            throw InvalidProtocol();
        }

        var parsed = new List<McpToolMetadata>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in tools.EnumerateObject())
        {
            var tool = property.Value;
            if (tool.ValueKind != JsonValueKind.Object ||
                !tool.TryGetProperty("inputSchema", out var inputSchema))
            {
                throw InvalidProtocol();
            }

            var name = RequiredString(tool, "name");
            if (!names.Add(name))
            {
                throw InvalidProtocol();
            }

            parsed.Add(new McpToolMetadata(
                name,
                OptionalString(tool, "title"),
                OptionalString(tool, "description"),
                inputSchema.Clone(),
                OptionalJson(tool, "outputSchema"),
                OptionalJson(tool, "annotations"),
                OptionalJson(tool, "icons"),
                OptionalJson(tool, "_meta")));
        }

        return parsed.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
    }

    internal static void EnsureRequiredServersAvailable(
        IReadOnlyList<ResolvedMcpServer> servers,
        IReadOnlyDictionary<string, DiscoveredMcpServer> discovered)
    {
        foreach (var required in servers.Where(server => server.Required))
        {
            if (!discovered.TryGetValue(ConfigServerName(required.Definition.Id), out var status) ||
                status.ServerInfo is null)
            {
                throw new CodexUnavailableException("A required MCP server is unavailable.");
            }
        }
    }

    private static async Task<JsonElement> SendRequestAsync(
        Process process,
        BoundedJsonLineReader reader,
        long id,
        string method,
        object parameters,
        ContainerRunHandle handle,
        CancellationToken cancellationToken)
    {
        await WriteMessageAsync(process, new { method, id, @params = parameters }, cancellationToken);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            handle.MarkContainerObserved();
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidProtocol();
            }

            if (!root.TryGetProperty("id", out var responseId))
            {
                if (root.TryGetProperty("method", out var notificationMethod) &&
                    notificationMethod.ValueKind == JsonValueKind.String)
                {
                    continue;
                }

                throw InvalidProtocol();
            }

            if (!responseId.TryGetInt64(out var value) || value != id)
            {
                throw InvalidProtocol();
            }

            if (root.TryGetProperty("error", out _))
            {
                throw new CodexUnavailableException("Codex rejected MCP metadata discovery.");
            }

            if (!root.TryGetProperty("result", out var result))
            {
                throw InvalidProtocol();
            }

            return result.Clone();
        }

        throw new CodexUnavailableException("The MCP metadata discovery container stopped unexpectedly.");
    }

    private static async Task WriteMessageAsync(
        Process process,
        object message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw InvalidProtocol();
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw InvalidProtocol();
        }

        return value.GetString();
    }

    private static JsonElement? OptionalJson(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.Clone();
    }

    private static string ConfigServerName(string serverId) => serverId.Replace('-', '_');

    private static CodexUnavailableException InvalidProtocol() =>
        new("Codex returned an invalid MCP metadata response.");

    private static async Task<string> ReadAndDrainLimitedAsync(StreamReader reader, int maximumCharacters)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                return result.ToString();
            }

            if (result.Length < maximumCharacters)
            {
                result.Append(buffer, 0, Math.Min(read, maximumCharacters - result.Length));
            }
        }
    }

    private static async Task ObserveAfterTerminationAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
    }

    internal sealed class BoundedJsonLineReader(Stream stream, int maximumBytes)
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly byte[] _buffer = new byte[8192];
        private int _bufferLength;
        private int _bufferOffset;
        private int _totalBytes;

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            using var line = new MemoryStream();
            while (true)
            {
                if (_bufferOffset == _bufferLength)
                {
                    _bufferLength = await stream.ReadAsync(_buffer, cancellationToken);
                    _bufferOffset = 0;
                    if (_bufferLength == 0)
                    {
                        return line.Length == 0 ? null : Decode(line);
                    }
                }

                var newline = Array.IndexOf(_buffer, (byte)'\n', _bufferOffset, _bufferLength - _bufferOffset);
                var end = newline >= 0 ? newline : _bufferLength;
                var count = end - _bufferOffset;
                AddBytes(count + (newline >= 0 ? 1 : 0), line.Length + count);
                if (count > 0)
                {
                    line.Write(_buffer, _bufferOffset, count);
                }

                _bufferOffset = newline >= 0 ? newline + 1 : _bufferLength;
                if (newline >= 0)
                {
                    return Decode(line);
                }
            }
        }

        private void AddBytes(int consumed, long lineBytes)
        {
            if (consumed > maximumBytes - _totalBytes || lineBytes > maximumBytes)
            {
                throw new InvalidDataException("MCP metadata output exceeded its limit.");
            }

            _totalBytes += consumed;
        }

        private static string Decode(MemoryStream line)
        {
            var length = checked((int)line.Length);
            var bytes = line.GetBuffer();
            if (length > 0 && bytes[length - 1] == (byte)'\r')
            {
                length--;
            }

            return StrictUtf8.GetString(bytes, 0, length);
        }
    }
}
