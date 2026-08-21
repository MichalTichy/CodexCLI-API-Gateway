using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CodexGateway.Infrastructure.Mcp;
using CodexGateway.Logic.Codex;
using CodexGateway.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodexGateway.Tests;

public sealed class GatewayMcpSessionManagerTests
{
    [Fact]
    public async Task Http_session_replaces_runner_token_with_gateway_api_key_and_expires_with_its_lease()
    {
        const string secretVariable = "CODEX_GATEWAY_MCP_TEST_SECRET";
        const string apiKey = "upstream-api-key";
        var previousValue = Environment.GetEnvironmentVariable(secretVariable);
        Environment.SetEnvironmentVariable(secretVariable, apiKey);
        try
        {
            var upstream = new RecordingHttpMessageHandler();
            var sessions = CreateManager(upstream);
            var server = new McpServerDefinition
            {
                Id = "remote-tools",
                Name = "Remote tools",
                ExecutionMode = McpExecutionMode.Gateway,
                Transport = McpTransport.Http,
                Url = "https://mcp.example.test/stream",
                BearerTokenEnvironmentVariable = secretVariable
            };

            await using var lease = await sessions.CreateAsync(
                Path.GetTempPath(),
                [new ResolvedMcpServer(server, [], false)],
                CancellationToken.None);
            var connection = Assert.Single(lease.Connections).Value!;
            var url = connection.Url!;
            var bearerToken = connection.BearerToken!;

            var context = CreateRequest(
                url,
                bearerToken,
                """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
            await sessions.HandleAsync(
                context,
                new Uri(url).Segments.Last().TrimEnd('/'),
                CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.NotNull(upstream.Authorization);
            Assert.Equal("Bearer", upstream.Authorization.Scheme);
            Assert.Equal(apiKey, upstream.Authorization.Parameter);
            Assert.NotEqual(connection.BearerToken, upstream.Authorization.Parameter);
            Assert.Equal("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", upstream.Body);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretVariable, previousValue);
        }
    }

    [Fact]
    public async Task Http_session_rejects_a_request_without_its_run_scoped_token()
    {
        const string secretVariable = "CODEX_GATEWAY_MCP_UNAUTHORIZED_TEST_SECRET";
        var previousValue = Environment.GetEnvironmentVariable(secretVariable);
        Environment.SetEnvironmentVariable(secretVariable, "upstream-api-key");
        try
        {
            var upstream = new RecordingHttpMessageHandler();
            var sessions = CreateManager(upstream);
            var server = new McpServerDefinition
            {
                Id = "remote-tools",
                Name = "Remote tools",
                ExecutionMode = McpExecutionMode.Gateway,
                Transport = McpTransport.Http,
                Url = "https://mcp.example.test/stream",
                BearerTokenEnvironmentVariable = secretVariable
            };

            await using var lease = await sessions.CreateAsync(
                Path.GetTempPath(),
                [new ResolvedMcpServer(server, [], false)],
                CancellationToken.None);
            var connection = Assert.Single(lease.Connections).Value!;
            var url = connection.Url!;
            var context = CreateRequest(url, "incorrect-token", "{}");

            await sessions.HandleAsync(
                context,
                new Uri(url).Segments.Last().TrimEnd('/'),
                CancellationToken.None);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.Null(upstream.Authorization);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretVariable, previousValue);
        }
    }

    [Fact]
    public async Task Local_stdio_session_relays_json_rpc_to_the_gateway_process()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "codex-gateway-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var server = new McpServerDefinition
            {
                Id = "local-tools",
                Name = "Local tools",
                ExecutionMode = McpExecutionMode.Gateway,
                Transport = McpTransport.Stdio,
                Command = "node",
                Arguments = [Path.Combine(AppContext.BaseDirectory, "test-assets", "mcp-smoke-server.mjs")]
            };
            var sessions = CreateManager(new RecordingHttpMessageHandler());
            var lease = await sessions.CreateAsync(
                workspace,
                [new ResolvedMcpServer(server, [], false)],
                CancellationToken.None);
            try
            {
                var connection = Assert.Single(lease.Connections).Value!;
                var url = connection.Url!;
                var context = CreateRequest(
                    url,
                    connection.BearerToken!,
                    """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""");

                await sessions.HandleAsync(
                    context,
                    new Uri(url).Segments.Last().TrimEnd('/'),
                    CancellationToken.None);

                context.Response.Body.Position = 0;
                var response = await new StreamReader(context.Response.Body).ReadToEndAsync();
                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
                Assert.Contains("codex-gateway-mcp-smoke", response, StringComparison.Ordinal);
            }
            finally
            {
                await lease.DisposeAsync();
            }
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static GatewayMcpSessionManager CreateManager(HttpMessageHandler upstream) =>
        new(
            new TestHttpClientFactory(upstream),
            Options.Create(new GatewayMcpOptions
            {
                RunnerBaseUrl = "http://gateway.test",
                SessionLifetimeMinutes = 60
            }),
            NullLogger<GatewayMcpSessionManager>.Instance);

    private static DefaultHttpContext CreateRequest(string url, string token, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new Uri(url).AbsolutePath;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Authorization = $"Bearer {token}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler upstream) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(upstream, disposeHandler: false);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":{"tools":[]}}""")
            };
        }
    }
}
