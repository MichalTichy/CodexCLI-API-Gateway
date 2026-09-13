using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex.Containers;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Codex.Models;

public sealed class CodexModelCatalog(
    ContainerRuntime runtime,
    IWorkspaceManager workspaces,
    IOptions<CodexOptions> options) : ICodexModelCatalog
{
    private const int PageSize = 100;
    private const int MaximumPages = 100;
    private const int MaximumOutputBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(options.Value.ModelDiscoveryTimeoutSeconds);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(options.Value.ModelCatalogRefreshSeconds);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _cacheLock = new();
    private CachedCatalog? _cache;

    public async Task<IReadOnlyList<CodexModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        if (TryGetCached(out var cached))
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(out cached))
            {
                return cached;
            }

            var models = await DiscoverAsync(cancellationToken);
            lock (_cacheLock)
            {
                _cache = new CachedCatalog(models, DateTimeOffset.UtcNow.Add(_refreshInterval));
            }

            return models;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    internal void Invalidate()
    {
        lock (_cacheLock)
        {
            _cache = null;
        }
    }

    internal static IReadOnlyList<CodexModel> ParsePage(
        JsonElement result,
        ISet<string> seenModelIds,
        out string? nextCursor)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProtocol();
        }

        var models = new List<CodexModel>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw InvalidProtocol();
            }

            if (item.TryGetProperty("hidden", out var hidden))
            {
                if (hidden.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw InvalidProtocol();
                }

                if (hidden.GetBoolean())
                {
                    continue;
                }
            }

            var modelId = RequiredString(item, "model");
            if (!seenModelIds.Add(modelId))
            {
                throw InvalidProtocol();
            }

            var efforts = ParseReasoningEfforts(item);
            var defaultEffort = RequiredString(item, "defaultReasoningEffort");
            if (!efforts.Contains(defaultEffort, StringComparer.Ordinal))
            {
                throw InvalidProtocol();
            }

            models.Add(new CodexModel(
                modelId,
                RequiredString(item, "displayName"),
                efforts,
                defaultEffort));
        }

        nextCursor = OptionalString(result, "nextCursor");
        return models;
    }

    private bool TryGetCached(out IReadOnlyList<CodexModel> models)
    {
        lock (_cacheLock)
        {
            if (_cache is { } cache && cache.RefreshAfter > DateTimeOffset.UtcNow)
            {
                models = cache.Models;
                return true;
            }
        }

        models = [];
        return false;
    }

    private async Task<IReadOnlyList<CodexModel>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaces.CreateEmptyAsync(cancellationToken);
        try
        {
            return await DiscoverInContainerAsync(workspace, cancellationToken);
        }
        finally
        {
            workspaces.Delete(workspace);
        }
    }

    private async Task<IReadOnlyList<CodexModel>> DiscoverInContainerAsync(
        RunWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var containerName = ContainerRuntime.CreateContainerName();
        var startInfo = runtime.CreateModelDiscoveryStartInfo(workspace, containerName);
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
            throw new CodexUnavailableException("The Codex model-discovery container could not be started.");
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
                        name = "codex-cli-api-gateway-model-discovery",
                        title = "Codex CLI API Gateway Model Discovery",
                        version = typeof(CodexModelCatalog).Assembly.GetName().Version?.ToString() ?? "0.1.0"
                    }
                },
                handle,
                linkedCancellation.Token);
            await WriteMessageAsync(
                process,
                new { method = "initialized", @params = new { } },
                linkedCancellation.Token);

            var models = new List<CodexModel>();
            var modelIds = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            for (var page = 0; page < MaximumPages; page++)
            {
                var result = await SendRequestAsync(
                    process,
                    reader,
                    ++requestId,
                    "model/list",
                    new { cursor, limit = PageSize, includeHidden = false },
                    handle,
                    linkedCancellation.Token);
                models.AddRange(ParsePage(result, modelIds, out var nextCursor));
                if (nextCursor is null)
                {
                    if (models.Count == 0)
                    {
                        throw new CodexUnavailableException(
                            "Codex did not report any models for the authenticated account.");
                    }

                    if (!await handle.TerminateAsync())
                    {
                        throw new CodexUnavailableException(
                            "The Codex model-discovery container could not be confirmed as stopped.");
                    }

                    await ObserveAfterTerminationAsync(stderrTask);
                    return models.ToArray();
                }

                if (!cursors.Add(nextCursor))
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
            throw new CodexUnavailableException("Codex model discovery timed out.");
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
                throw new CodexUnavailableException("Codex rejected model discovery.");
            }

            if (!root.TryGetProperty("result", out var result))
            {
                throw InvalidProtocol();
            }

            return result.Clone();
        }

        throw new CodexUnavailableException("The Codex model-discovery container stopped unexpectedly.");
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

    private static IReadOnlyList<string> ParseReasoningEfforts(JsonElement model)
    {
        if (!model.TryGetProperty("supportedReasoningEfforts", out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProtocol();
        }

        var efforts = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw InvalidProtocol();
            }

            var effort = RequiredString(value, "reasoningEffort");
            if (!efforts.Contains(effort, StringComparer.Ordinal))
            {
                efforts.Add(effort);
            }
        }

        if (efforts.Count == 0)
        {
            throw InvalidProtocol();
        }

        return efforts.ToArray();
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

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw InvalidProtocol();
        }

        return value.GetString();
    }

    private static CodexUnavailableException InvalidProtocol() =>
        new("Codex returned an invalid model catalog response.");

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

    private sealed record CachedCatalog(IReadOnlyList<CodexModel> Models, DateTimeOffset RefreshAfter);

    private sealed class BoundedJsonLineReader(Stream stream, int maximumBytes)
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
                throw new InvalidDataException("Codex model catalog output exceeded its limit.");
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
