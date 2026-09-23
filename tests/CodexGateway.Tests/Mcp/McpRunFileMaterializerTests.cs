using System.Text;
using System.Text.Json;
using CodexGateway.McpGateway.Configuration.Models;
using CodexGateway.McpGateway.Errors;
using CodexGateway.McpGateway.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodexGateway.Tests.Mcp;

public sealed class McpRunFileMaterializerTests
{
    [Fact]
    public async Task Artifact_resource_is_replaced_by_bounded_run_local_file_reference()
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var bytes = "invoice contents"u8.ToArray();
            var response = Response(
                "artifact://email/invoice%2042.pdf",
                "application/pdf",
                bytes);

            var transformed = await materializer.MaterializeAsync(
                response,
                "application/json",
                CancellationToken.None);

            var text = GetResultText(transformed);
            Assert.DoesNotContain(Convert.ToBase64String(bytes), text, StringComparison.Ordinal);
            Assert.Contains("invoice 42.pdf", text, StringComparison.Ordinal);
            Assert.Contains("application/pdf", text, StringComparison.Ordinal);
            Assert.Contains("./.gateway/mcp-files/", text, StringComparison.Ordinal);

            var relativePath = JsonDocument.Parse(text[(text.IndexOf('{'))..])
                .RootElement.GetProperty("path").GetString()!;
            var fullPath = Path.Combine(
                workspace,
                relativePath[2..].Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(fullPath));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Non_artifact_binary_resource_is_forwarded_unchanged()
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var response = Response(
                "https://example.test/file.bin",
                "application/octet-stream",
                [1, 2, 3]);

            var transformed = await materializer.MaterializeAsync(
                response,
                "application/json",
                CancellationToken.None);

            Assert.Equal(response, transformed);
            Assert.False(Directory.Exists(Path.Combine(workspace, ".gateway", "mcp-files")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Oversized_artifact_resource_is_rejected_before_it_is_written()
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace, maxFileMegabytes: 1);
            var response = Response(
                "artifact://email/too-large.bin",
                "application/octet-stream",
                new byte[(1024 * 1024) + 1]);

            var exception = await Assert.ThrowsAsync<GatewayMcpRequestException>(() =>
                materializer.MaterializeAsync(response, "application/json", CancellationToken.None));

            Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
            Assert.False(Directory.Exists(Path.Combine(workspace, ".gateway", "mcp-files")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Artifact_resource_inside_event_stream_is_materialized()
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var json = Encoding.UTF8.GetString(Response(
                "artifact://email/notes.txt",
                "text/plain",
                "hello"u8.ToArray()));
            var response = Encoding.UTF8.GetBytes($"event: message\ndata: {json}\n\n");

            var transformed = await materializer.MaterializeAsync(
                response,
                "text/event-stream",
                CancellationToken.None);
            var text = Encoding.UTF8.GetString(transformed);

            Assert.Contains("./.gateway/mcp-files/", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String("hello"u8.ToArray()), text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static McpRunFileMaterializer CreateMaterializer(
        string workspace,
        int maxFileMegabytes = 25) =>
        new(
            workspace,
            new GatewayMcpOptions
            {
                MaxMaterializedFiles = 10,
                MaxMaterializedFileMegabytes = maxFileMegabytes,
                MaxMaterializedTotalMegabytes = 50
            },
            NullLogger.Instance);

    private static byte[] Response(string uri, string mimeType, byte[] bytes) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                content = new object[]
                {
                    new
                    {
                        type = "resource",
                        resource = new
                        {
                            uri,
                            mimeType,
                            blob = Convert.ToBase64String(bytes)
                        }
                    }
                }
            }
        });

    private static string GetResultText(byte[] response)
    {
        using var document = JsonDocument.Parse(response);
        return document.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "codex-gateway-mcp-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
