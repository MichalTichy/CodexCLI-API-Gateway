using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.FileStorage;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Errors;
using CodexGateway.Models;

namespace CodexGateway.Infrastructure.Codex.Containers;

public sealed class ContainerCodexRunner(
    ContainerRuntime runtime,
    RunArtifactQuotaMonitor artifactQuotaMonitor,
    IGatewayMcpSessionFactory gatewayMcpSessionFactory) : ICodexRunner
{
    internal const string RunPermissionProfile = "gateway_run";

    public async Task<CodexRunResult> RunAsync(
        CodexRunRequest request,
        Func<string, CancellationToken, Task>? onText,
        CancellationToken cancellationToken)
    {
        var containerName = ContainerRuntime.CreateContainerName();
        await using var gatewayLease = await gatewayMcpSessionFactory.CreateAsync(
            request.Workspace.RootPath,
            request.McpServers,
            cancellationToken);
        var startInfo = runtime.CreateRunStartInfo(request, containerName, gatewayLease.Connections);
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
            throw new CodexUnavailableException("The Codex runner container could not be started.");
        }

        var stderrTask = ReadAndDrainLimitedAsync(process.StandardError, 64 * 1024);
        using var quotaMonitorCancellation = new CancellationTokenSource();
        var quotaMonitorTask = artifactQuotaMonitor.MonitorAsync(
            request.Workspace.RootPath,
            handle.TerminateBlocking,
            quotaMonitorCancellation.Token);
        string? lastCompletedAgentMessage = null;
        var failureDiagnostics = new StringBuilder();
        var malformedOutput = false;
        var runFailed = false;
        var turnCompleted = false;
        var usage = CodexUsage.Empty;
        var stderr = string.Empty;
        try
        {
            await process.StandardInput.WriteAsync(request.Prompt.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                handle.MarkContainerObserved();
                try
                {
                    using var eventDocument = JsonDocument.Parse(line);
                    if (eventDocument.RootElement.TryGetProperty("type", out var eventType))
                    {
                        if (eventType.GetString() is "turn.failed" or "error")
                        {
                            runFailed = true;
                            AppendLimited(failureDiagnostics, line, 64 * 1024);
                        }

                        turnCompleted |= eventType.GetString() == "turn.completed";
                        if (eventType.GetString() == "turn.completed" &&
                            eventDocument.RootElement.TryGetProperty("usage", out var usageElement))
                        {
                            usage = new CodexUsage(
                                GetInt32(usageElement, "input_tokens"),
                                GetInt32(usageElement, "output_tokens"),
                                GetInt32(usageElement, "cached_input_tokens"),
                                GetInt32(usageElement, "reasoning_output_tokens"));
                        }
                    }
                }
                catch (JsonException)
                {
                    malformedOutput = true;
                }

                if (TryReadAgentText(line, out var text))
                {
                    lastCompletedAgentMessage = text;
                }

                if (turnCompleted || runFailed)
                {
                    // The terminal JSONL event is the commit boundary. Force-removing the whole
                    // container also stops any descendants before project artifacts are committed.
                    if (!await handle.TerminateAsync())
                    {
                        throw new CodexUnavailableException(
                            "The Codex runner container could not be confirmed as stopped.");
                    }
                    break;
                }
            }

            await process.WaitForExitAsync(cancellationToken);
            stderr = await stderrTask.WaitAsync(cancellationToken);
            artifactQuotaMonitor.Check(request.Workspace.RootPath, cancellationToken);
            var quotaFailure = await StopQuotaMonitorAsync(quotaMonitorTask, quotaMonitorCancellation);
            if (quotaFailure is not null)
            {
                throw quotaFailure;
            }
        }
        catch (OperationCanceledException)
        {
            await handle.TerminateAsync();
            quotaMonitorCancellation.Cancel();
            await ObserveAfterTerminationAsync(stderrTask);
            await ObserveAfterTerminationAsync(quotaMonitorTask);
            throw;
        }
        catch
        {
            await handle.TerminateAsync();
            var quotaFailure = await StopQuotaMonitorAsync(quotaMonitorTask, quotaMonitorCancellation);
            await ObserveAfterTerminationAsync(stderrTask);
            if (quotaFailure is not null)
            {
                throw quotaFailure;
            }

            throw;
        }
        finally
        {
            quotaMonitorCancellation.Cancel();
            await handle.TerminateAsync();
            await ObserveAfterTerminationAsync(stderrTask);
            await ObserveAfterTerminationAsync(quotaMonitorTask);
        }

        if (runFailed || malformedOutput || !turnCompleted)
        {
            if (LooksUnavailable(failureDiagnostics.ToString()) || LooksUnavailable(stderr))
            {
                throw new CodexUnavailableException("Codex or a required MCP server is unavailable.");
            }

            throw new CodexFailedException(
                runFailed
                    ? CodexFailureReason.TurnFailed
                    : malformedOutput
                        ? CodexFailureReason.MalformedOutput
                        : CodexFailureReason.IncompleteTurn);
        }

        if (string.IsNullOrWhiteSpace(lastCompletedAgentMessage))
        {
            throw new CodexFailedException(CodexFailureReason.MissingAgentResponse);
        }

        if (request.OutputSchema is not null && !IsValidJsonDocument(lastCompletedAgentMessage))
        {
            throw new CodexFailedException(CodexFailureReason.InvalidStructuredOutput);
        }

        if (onText is not null)
        {
            await onText(lastCompletedAgentMessage, cancellationToken);
        }

        return new CodexRunResult(lastCompletedAgentMessage, usage);
    }

    internal static void AddMcpConfiguration(
        ICollection<string> arguments,
        ResolvedMcpServer resolved,
        GatewayMcpRunnerConnection? connection = null,
        bool includeToolFilter = true)
    {
        var server = resolved.Definition;
        var key = "mcp_servers." + server.Id.Replace('-', '_');
        ContainerCommandBuilder.AddConfig(arguments, $"{key}.enabled=true");
        ContainerCommandBuilder.AddConfig(arguments, $"{key}.required={resolved.Required.ToString().ToLowerInvariant()}");
        ContainerCommandBuilder.AddConfig(arguments, $"{key}.default_tools_approval_mode=\"approve\"");
        if (server is HttpMcpServerDefinition http)
        {
            ContainerCommandBuilder.AddConfig(arguments, $"{key}.url={TomlString(connection?.Url ?? http.Url)}");
            if (connection is not null)
            {
                ContainerCommandBuilder.AddConfig(
                    arguments,
                    $"{key}.bearer_token_env_var={TomlString(connection.SessionTokenEnvironmentVariable)}");
            }
            else if (http.EnvironmentHeaders.Count > 0)
            {
                ContainerCommandBuilder.AddConfig(
                    arguments,
                    $"{key}.env_http_headers={TomlTable(http.EnvironmentHeaders
                        .Select(header => (header.Key, header.Value))
                        .ToArray())}");
            }
        }
        else if (server is StdioMcpServerDefinition stdio)
        {
            ContainerCommandBuilder.AddConfig(arguments, $"{key}.command={TomlString(stdio.Command)}");
            ContainerCommandBuilder.AddConfig(arguments, $"{key}.args={TomlArray(stdio.Arguments)}");
            if (stdio.EnvironmentVariables.Count > 0)
            {
                ContainerCommandBuilder.AddConfig(arguments, $"{key}.env_vars={TomlArray(stdio.EnvironmentVariables)}");
            }
        }
        else
        {
            throw new InvalidOperationException($"MCP server '{server.Id}' has an unsupported transport.");
        }

        if (includeToolFilter)
        {
            ContainerCommandBuilder.AddConfig(arguments, $"{key}.enabled_tools={TomlArray(resolved.EnabledTools)}");
        }
    }

    internal static string TomlString(string value) => JsonSerializer.Serialize(value);

    private static string TomlArray(IEnumerable<string> values) =>
        "[" + string.Join(',', values.Select(TomlString)) + "]";

    internal static string BuildFileSystemPermissionTable() =>
        "{" +
        $"{TomlString(":minimal")}=\"read\"," +
        $"{TomlString(":tmpdir")}=\"write\"," +
        $"{TomlString(":slash_tmp")}=\"write\"," +
        $"{TomlString(":workspace_roots")}=\"write\"" +
        "}";

    internal static string TomlTable(params (string Key, string Value)[] entries) =>
        "{" + string.Join(',', entries.Select(entry =>
            $"{TomlString(entry.Key)}={TomlString(entry.Value)}")) + "}";

    internal static bool TryReadAgentText(string jsonLine, out string text)
    {
        text = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type))
            {
                return false;
            }

            if (type.GetString() == "item.completed" &&
                root.TryGetProperty("item", out var item) &&
                item.TryGetProperty("type", out var itemType) &&
                itemType.GetString() == "agent_message" &&
                item.TryGetProperty("text", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                text = message.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (JsonException)
        {
            // Non-JSON output is deliberately ignored and never returned to API clients.
        }

        return false;
    }

    internal static bool IsValidJsonDocument(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int GetInt32(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static async Task<string> ReadAndDrainLimitedAsync(StreamReader reader, int maximumCharacters)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            if (result.Length < maximumCharacters)
            {
                result.Append(buffer, 0, Math.Min(read, maximumCharacters - result.Length));
            }
        }

        return result.ToString();
    }

    private static void AppendLimited(StringBuilder builder, string value, int maximumCharacters)
    {
        if (builder.Length >= maximumCharacters)
        {
            return;
        }

        builder.Append(value.AsSpan(0, Math.Min(value.Length, maximumCharacters - builder.Length)));
        builder.AppendLine();
    }

    private static bool LooksUnavailable(string diagnostics)
    {
        string[] markers =
        [
            "not logged in",
            "authentication",
            "unauthorized",
            "invalid api key",
            "required mcp",
            "mcp server",
            "failed to connect",
            "connection refused",
            "service unavailable"
        ];
        return markers.Any(marker => diagnostics.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ObserveAfterTerminationAsync(Task<string> stderrTask)
    {
        try
        {
            await stderrTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
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

    private static async Task<GatewayException?> StopQuotaMonitorAsync(
        Task quotaMonitorTask,
        CancellationTokenSource cancellation)
    {
        cancellation.Cancel();
        try
        {
            await quotaMonitorTask;
            return null;
        }
        catch (GatewayException exception) when (exception.Code == "artifact_limit_exceeded")
        {
            return exception;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
    }
}
