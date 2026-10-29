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
    public async Task Generated_file_is_snapshotted_into_a_download_url_before_forwarding_the_call()
    {
        var workspace = CreateWorkspace();
        try
        {
            var directory = Path.Combine(workspace, "artifacts", "Notino");
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "report.xlsx");
            var bytes = new byte[] { 80, 75, 3, 4, 0, 255 };
            await File.WriteAllBytesAsync(source, bytes);
            var materializer = CreateMaterializer(workspace);
            var request = FileRequest("./artifacts/Notino/report.xlsx");

            var prepared = await materializer.PrepareRequestAsync(request, CancellationToken.None);
            using var document = JsonDocument.Parse(prepared);
            var url = document.RootElement.GetProperty("params").GetProperty("arguments")
                .GetProperty("files")[0].GetProperty("agentFileUrl").GetString()!;
            await File.WriteAllTextAsync(source, "changed after the call");
            var artifact = await materializer.ReadArtifactAsync(new Uri(url).Segments.Last(), CancellationToken.None);

            Assert.NotNull(artifact);
            Assert.Equal(bytes, artifact.Value.Bytes);
            Assert.Equal("report.xlsx", artifact.Value.FileName);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", artifact.Value.MimeType);
            Assert.DoesNotContain(Convert.ToBase64String(bytes), Encoding.UTF8.GetString(prepared), StringComparison.Ordinal);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    [Theory]
    [InlineData("./artifacts/../secret.txt")]
    [InlineData("./artifacts/sub/../../secret.txt")]
    public async Task Generated_file_cannot_escape_the_artifacts_directory(string path)
    {
        var workspace = CreateWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(workspace, "artifacts"));
            await File.WriteAllTextAsync(Path.Combine(workspace, "secret.txt"), "private");
            await Assert.ThrowsAsync<GatewayMcpRequestException>(() =>
                CreateMaterializer(workspace).PrepareRequestAsync(FileRequest(path), CancellationToken.None));
            Assert.False(Directory.Exists(Path.Combine(workspace, ".gateway", "mcp-files")));
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    [Fact]
    public async Task Oversized_generated_file_is_not_exported()
    {
        var workspace = CreateWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(workspace, "artifacts"));
            await File.WriteAllBytesAsync(Path.Combine(workspace, "artifacts", "large.xlsx"), new byte[1024 * 1024 + 1]);
            var exception = await Assert.ThrowsAsync<GatewayMcpRequestException>(() =>
                CreateMaterializer(workspace, 1).PrepareRequestAsync(FileRequest("./artifacts/large.xlsx"), CancellationToken.None));
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
            Assert.False(Directory.Exists(Path.Combine(workspace, ".gateway", "mcp-files")));
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    [Fact]
    public async Task Existing_artifact_urls_are_forwarded_unchanged()
    {
        var workspace = CreateWorkspace();
        try
        {
            var request = FileRequest("http://gateway.test/_internal/mcp/artifacts/token");
            var prepared = await CreateMaterializer(workspace).PrepareRequestAsync(request, CancellationToken.None);
            Assert.Equal(request, prepared);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    private static byte[] FileRequest(string path) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        jsonrpc = "2.0", id = 1, method = "tools/call",
        @params = new { name = "teams_send_chat_message_with_attachments", arguments = new { files = new[] { new { name = "report.xlsx", agentFileUrl = path } } } }
    });

    [Theory]
    [InlineData("artifact://email/invoice%2042.pdf", "application/pdf", "invoice 42.pdf")]
    [InlineData("https://files.todoist.com/user_upload/v2/2248028139/file.xlsx", "application/octet-stream", "file.xlsx")]
    [InlineData("https://files.todoist.com/user_upload/v2/2248028144/file.docx", "application/octet-stream", "file.docx")]
    public async Task Binary_resource_is_replaced_by_bounded_run_local_file_reference(
        string uri, string mimeType, string fileName)
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var bytes = "invoice contents"u8.ToArray();
            var response = Response(uri, mimeType, bytes);

            var transformed = await materializer.MaterializeAsync(
                response,
                "application/json",
                CancellationToken.None);

            var text = GetResultText(transformed);
            Assert.DoesNotContain(Convert.ToBase64String(bytes), text, StringComparison.Ordinal);
            Assert.Contains(fileName, text, StringComparison.Ordinal);
            Assert.Contains(mimeType, text, StringComparison.Ordinal);
            Assert.Contains("./.gateway/mcp-files/", text, StringComparison.Ordinal);
            Assert.Contains("http://host.docker.internal:8080/_internal/mcp/artifacts/", text, StringComparison.Ordinal);

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

    [Theory]
    [InlineData("artifact://invoice/invoice.pdf")]
    [InlineData("https://files.todoist.com/user_upload/v2/123/invoice.pdf")]
    public async Task Artifact_url_returns_original_bytes_without_model_base64(string uri)
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var bytes = "invoice contents"u8.ToArray();
            var response = await materializer.MaterializeAsync(
                Response(uri, "application/pdf", bytes),
                "application/json",
                CancellationToken.None);
            var text = GetResultText(response);
            var metadata = JsonDocument.Parse(text[text.IndexOf('{')..]);
            var url = metadata.RootElement.GetProperty("agentFileUrl").GetString()!;
            var token = new Uri(url).Segments.Last();

            var artifact = await materializer.ReadArtifactAsync(token, CancellationToken.None);

            Assert.NotNull(artifact);
            Assert.Equal(bytes, artifact.Value.Bytes);
            Assert.Equal("invoice.pdf", artifact.Value.FileName);
            Assert.Equal("application/pdf", artifact.Value.MimeType);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task A_modified_file_cannot_be_served_as_an_http_artifact()
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var response = await materializer.MaterializeAsync(
                Response("artifact://invoice/invoice.pdf", "application/pdf", "original"u8.ToArray()),
                "application/json",
                CancellationToken.None);
            var metadata = JsonDocument.Parse(GetResultText(response)[GetResultText(response).IndexOf('{')..]);
            var token = new Uri(metadata.RootElement.GetProperty("agentFileUrl").GetString()!).Segments.Last();
            var path = metadata.RootElement.GetProperty("path").GetString()!;
            await File.WriteAllTextAsync(Path.Combine(workspace, path[2..]), "tampered");
            var exception = await Assert.ThrowsAsync<GatewayMcpRequestException>(() =>
                materializer.ReadArtifactAsync(token, CancellationToken.None));

            Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{"type":"resource","resource":{"uri":"https://files.todoist.com/file.txt","mimeType":"text/plain","text":"source text"}}""")]
    [InlineData("""{"type":"resource_link","uri":"https://files.todoist.com/file.xlsx","name":"file.xlsx"}""")]
    public async Task Https_resources_without_embedded_bytes_are_forwarded_unchanged(string contentJson)
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var response = Encoding.UTF8.GetBytes(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[" + contentJson + "]}}");

            var transformed = await materializer.MaterializeAsync(
                response, "application/json", CancellationToken.None);

            Assert.Equal(response, transformed);
            Assert.False(Directory.Exists(Path.Combine(workspace, ".gateway", "mcp-files")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Theory]
    [InlineData("file:///private/file.bin")]
    [InlineData("http://example.test/file.bin")]
    [InlineData("custom://example/file.bin")]
    public async Task Unsupported_binary_resource_uri_is_forwarded_unchanged(string uri)
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var response = Response(
                uri,
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

    [Theory]
    [InlineData("artifact://email/too-large.bin")]
    [InlineData("https://files.todoist.com/user_upload/v2/123/file.xlsx")]
    public async Task Oversized_binary_resource_is_rejected_before_it_is_written(string uri)
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace, maxFileMegabytes: 1);
            var response = Response(
                uri,
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

    [Theory]
    [InlineData("artifact://email/notes.txt")]
    [InlineData("https://files.todoist.com/user_upload/v2/123/file.docx")]
    public async Task Binary_resource_inside_event_stream_is_materialized(string uri)
    {
        var workspace = CreateWorkspace();
        try
        {
            var materializer = CreateMaterializer(workspace);
            var json = Encoding.UTF8.GetString(Response(
                uri,
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
