using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Infrastructure.Mcp;

internal sealed class LocalStdioGatewayMcpUpstream : IGatewayMcpUpstream
{
    private static readonly string[] RequiredHostEnvironmentVariables =
    [
        "HOME", "LANG", "LC_ALL", "PATH", "SystemRoot", "TEMP", "TMP", "TMPDIR", "USERPROFILE"
    ];

    private readonly Process _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger;
    private readonly string _serverId;
    private readonly Task _stderrPump;

    private LocalStdioGatewayMcpUpstream(
        Process process,
        string serverId,
        ILogger logger)
    {
        _process = process;
        _serverId = serverId;
        _logger = logger;
        _stderrPump = PumpStandardErrorAsync();
    }

    public static Task<LocalStdioGatewayMcpUpstream> StartAsync(
        McpServerDefinition definition,
        string workspacePath,
        IReadOnlyDictionary<string, string> environment,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(definition.Command))
        {
            throw new InvalidOperationException(
                $"Gateway-hosted STDIO MCP server '{definition.Id}' does not specify a command.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = definition.Command,
            WorkingDirectory = workspacePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment.Clear();
        foreach (var name in RequiredHostEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                startInfo.Environment[name] = value;
            }
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        foreach (var argument in definition.Arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"Gateway-hosted STDIO MCP server '{definition.Id}' could not be started.");
        }

        return Task.FromResult(new LocalStdioGatewayMcpUpstream(process, definition.Id, logger));
    }

    public async Task ForwardAsync(
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status405MethodNotAllowed,
                "STDIO MCP servers only support POST requests.");
        }

        var payload = Encoding.UTF8.GetString(
            await GatewayMcpRequestBody.ReadAsync(request, cancellationToken));
        var requestId = GetRequestId(payload);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Gateway-hosted STDIO MCP server '{_serverId}' exited unexpectedly.");
            }

            await _process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
            if (requestId is null)
            {
                response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    throw new InvalidOperationException(
                        $"Gateway-hosted STDIO MCP server '{_serverId}' closed its output unexpectedly.");
                }

                if (IsResponseForRequest(line, requestId))
                {
                    response.ContentType = "application/json";
                    await response.WriteAsync(line, cancellationToken);
                    return;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync();
            await _stderrPump;
        }
        finally
        {
            _process.Dispose();
            _gate.Dispose();
        }
    }

    private async Task PumpStandardErrorAsync()
    {
        while (await _process.StandardError.ReadLineAsync() is not null)
        {
            _logger.LogDebug(
                "Gateway-hosted STDIO MCP server {McpServerId} wrote to standard error.",
                _serverId);
        }
    }

    private static string? GetRequestId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("id", out var id)
                ? id.GetRawText()
                : null;
        }
        catch (JsonException exception)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status400BadRequest,
                "The MCP request is not valid JSON-RPC.",
                exception);
        }
    }

    private static bool IsResponseForRequest(string payload, string requestId)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("id", out var id) &&
                   string.Equals(id.GetRawText(), requestId, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
