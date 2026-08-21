using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Mcp;

public sealed class GatewayMcpSessionManager(
    IHttpClientFactory httpClientFactory,
    IOptions<GatewayMcpOptions> options,
    ILogger<GatewayMcpSessionManager> logger)
    : IGatewayMcpSessionFactory, IGatewayMcpRequestHandler
{
    internal const string HttpClientName = "GatewayMcp";

    private readonly ConcurrentDictionary<string, GatewayMcpSession> _sessions =
        new(StringComparer.Ordinal);

    public async Task<GatewayMcpSessionLease> CreateAsync(
        string workspacePath,
        IReadOnlyList<ResolvedMcpServer> servers,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(servers);

        var gatewayServers = servers
            .Where(server => server.Definition.ExecutionMode == McpExecutionMode.Gateway)
            .ToArray();
        if (gatewayServers.Length == 0)
        {
            return new GatewayMcpSessionLease(
                new Dictionary<string, GatewayMcpRunnerConnection>(StringComparer.Ordinal),
                static () => ValueTask.CompletedTask);
        }

        var baseUri = GetRunnerBaseUri();
        var created = new List<string>(gatewayServers.Length);
        var connections = new Dictionary<string, GatewayMcpRunnerConnection>(StringComparer.Ordinal);
        try
        {
            foreach (var server in gatewayServers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sessionId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
                var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                var upstream = await CreateUpstreamAsync(server.Definition, workspacePath, cancellationToken);
                var session = new GatewayMcpSession(
                    token,
                    DateTimeOffset.UtcNow.AddMinutes(options.Value.SessionLifetimeMinutes),
                    upstream);
                if (!_sessions.TryAdd(sessionId, session))
                {
                    await upstream.DisposeAsync();
                    throw new InvalidOperationException("A generated gateway MCP session ID was already in use.");
                }

                created.Add(sessionId);
                connections.Add(
                    server.Definition.Id,
                    new GatewayMcpRunnerConnection(
                        new Uri(baseUri, $"_internal/mcp/{sessionId}").ToString(),
                        CreateTokenEnvironmentVariable(server.Definition.Id, sessionId),
                        token));
            }
        }
        catch
        {
            await RemoveAsync(created);
            throw;
        }

        return new GatewayMcpSessionLease(
            connections,
            () => new ValueTask(RemoveAsync(created)));
    }

    public async Task HandleAsync(
        HttpContext context,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            await WriteErrorAsync(context.Response, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await RemoveAsync([sessionId]);
            await WriteErrorAsync(context.Response, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (!HasValidBearerToken(context.Request, session.Token))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await WriteErrorAsync(context.Response, StatusCodes.Status401Unauthorized, cancellationToken);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        try
        {
            await session.Upstream.ForwardAsync(context.Request, context.Response, cancellationToken);
        }
        catch (GatewayMcpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Gateway MCP session {SessionId} rejected a runner request.",
                sessionId);
            await WriteErrorAsync(context.Response, exception.StatusCode, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Gateway MCP session {SessionId} could not reach its upstream server.",
                sessionId);
            await WriteErrorAsync(context.Response, StatusCodes.Status502BadGateway, cancellationToken);
        }
    }

    internal async Task RemoveExpiredAsync()
    {
        var expired = _sessions
            .Where(pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow)
            .Select(pair => pair.Key)
            .ToArray();
        await RemoveAsync(expired);
    }

    private async Task<IGatewayMcpUpstream> CreateUpstreamAsync(
        McpServerDefinition definition,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        return definition switch
        {
            HttpMcpServerDefinition http => new HttpGatewayMcpUpstream(
                httpClientFactory.CreateClient(HttpClientName),
                new Uri(http.Url, UriKind.Absolute),
                GetRequiredEnvironmentValue(http.BearerTokenEnvironmentVariable, definition.Id)),
            StdioMcpServerDefinition stdio => await LocalStdioGatewayMcpUpstream.StartAsync(
                stdio,
                workspacePath,
                GetEnvironmentValues(stdio.EnvironmentVariables),
                logger,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"MCP server '{definition.Id}' has an unsupported transport.")
        };
    }

    private Uri GetRunnerBaseUri()
    {
        if (!Uri.TryCreate(options.Value.RunnerBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "McpGateway:RunnerBaseUrl must be an absolute HTTP(S) URL.");
        }

        return new Uri(baseUri.ToString().TrimEnd('/') + "/");
    }

    private static string GetRequiredEnvironmentValue(string? name, string serverId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"Gateway-hosted HTTP MCP server '{serverId}' does not specify an API-key environment variable.");
        }

        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The API-key environment variable for gateway-hosted MCP server '{serverId}' is unavailable.");
        }

        return value;
    }

    private static IReadOnlyDictionary<string, string> GetEnvironmentValues(
        IEnumerable<string>? names)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in (names ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.Ordinal))
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                values.Add(name, value);
            }
        }

        return values;
    }

    private static string CreateTokenEnvironmentVariable(string serverId, string sessionId)
    {
        var normalizedServerId = new string(
            serverId.Select(character => char.IsAsciiLetterOrDigit(character)
                ? char.ToUpperInvariant(character)
                : '_').ToArray());
        return $"CODEX_GATEWAY_MCP_{normalizedServerId}_{sessionId[..8].ToUpperInvariant()}";
    }

    private static bool HasValidBearerToken(HttpRequest request, string expectedToken)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedToken = authorization[prefix.Length..].Trim();
        var supplied = Encoding.UTF8.GetBytes(suppliedToken);
        var expected = Encoding.UTF8.GetBytes(expectedToken);
        return supplied.Length == expected.Length &&
               CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static async Task WriteErrorAsync(
        HttpResponse response,
        int statusCode,
        CancellationToken cancellationToken)
    {
        if (response.HasStarted)
        {
            return;
        }

        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        await response.WriteAsync("""{"error":"Gateway MCP request failed."}""", cancellationToken);
    }

    private async Task RemoveAsync(IEnumerable<string> sessionIds)
    {
        foreach (var sessionId in sessionIds)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                await session.Upstream.DisposeAsync();
            }
        }
    }

    private sealed record GatewayMcpSession(
        string Token,
        DateTimeOffset ExpiresAt,
        IGatewayMcpUpstream Upstream);
}
