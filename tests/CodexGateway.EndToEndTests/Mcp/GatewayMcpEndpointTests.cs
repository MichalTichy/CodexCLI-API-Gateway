using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Mcp;

public sealed class GatewayMcpEndpointTests : IAsyncDisposable
{
    private const string ApiKeyEnvVar = "E2E_GATEWAY_MCP_API_KEY";
    private readonly string _previousApiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar) ?? string.Empty;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codex-gateway-mcp-e2e", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Internal_gateway_mcp_endpoint_uses_scoped_token_and_forwards_environment_header()
    {
        Directory.CreateDirectory(_root);
        var (upstreamUrl, capture, disposeUpstream) = await StartUpstreamAsync();
        try
        {
            Environment.SetEnvironmentVariable(ApiKeyEnvVar, "real-api-key-value");
            await using var factory = new GatewayFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var sessionFactory = factory.Services.GetRequiredService<IGatewayMcpSessionFactory>();

            var server = new HttpMcpServerDefinition
            {
                Id = "gateway-http",
                Name = "Gateway HTTP",
                ExecutionMode = McpExecutionMode.Gateway,
                Url = upstreamUrl,
                EnvironmentHeaders = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKeyEnvVar
                },
                AvailableTools = ["read"]
            };

            await using var lease = await sessionFactory.CreateAsync(
                _root,
                [new ResolvedMcpServer(server, ["read"], true)],
                CancellationToken.None);
            var connection = Assert.Single(lease.Connections).Value;
            var path = new Uri(connection.Url).PathAndQuery;

            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.SessionToken);
            request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" });

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var received = await capture.Task.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("real-api-key-value", received.ApiKey);
            Assert.NotEqual(connection.SessionToken, received.ApiKey);

            using var wrong = new HttpRequestMessage(HttpMethod.Post, path);
            wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
            wrong.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 2, method = "tools/list" });
            using var wrongResponse = await client.SendAsync(wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);

            using var missing = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new { jsonrpc = "2.0", id = 3, method = "tools/list" })
            };
            using var missingResponse = await client.SendAsync(missing);
            Assert.Equal(HttpStatusCode.Unauthorized, missingResponse.StatusCode);
            Assert.Equal(1, capture.Count);
        }
        finally
        {
            await disposeUpstream();
            Environment.SetEnvironmentVariable(ApiKeyEnvVar, _previousApiKey);
        }
    }

    private static async Task<(string Url, UpstreamCapture Capture, Func<Task> DisposeAsync)> StartUpstreamAsync()
    {
        var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        var capture = new UpstreamCapture();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    var body = await new StreamReader(context.Request.InputStream, context.Request.ContentEncoding).ReadToEndAsync();
                    capture.Record(context.Request.Headers["X-Api-Key"], body);
                    var payload = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(payload);
                    context.Response.Close();
                }
                catch (HttpListenerException)
                {
                    break;
                }
            }
        });
        return (prefix.TrimEnd('/'), capture, () => { listener.Stop(); listener.Close(); return Task.CompletedTask; });
    }

    private static int GetFreePort()
    {
        var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        socket.Start();
        var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    public ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(ApiKeyEnvVar, _previousApiKey);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
        return ValueTask.CompletedTask;
    }

    private sealed class UpstreamCapture
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public TaskCompletionSource<ReceivedRequest> Task { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Record(string? apiKey, string body)
        {
            if (Interlocked.Increment(ref _count) == 1)
            {
                Task.TrySetResult(new ReceivedRequest(apiKey, body));
            }
        }
    }

    private sealed record ReceivedRequest(string? ApiKey, string Body);
}
