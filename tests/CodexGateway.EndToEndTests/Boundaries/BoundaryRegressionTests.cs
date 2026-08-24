using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Boundaries;

public sealed class BoundaryRegressionTests : IDisposable
{
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;

    public BoundaryRegressionTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
    }

    [Theory]
    [InlineData("metadata.json")]
    [InlineData("MeTaDaTa.JsOn")]
    public async Task Projectless_payload_names_cannot_collide_with_internal_metadata(string fileName)
    {
        var expected = "payload named " + fileName;
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("assistants"), "purpose");
        upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(expected)), "file", fileName);

        using var uploaded = await _client.PostAsync("/v1/files", upload);
        uploaded.EnsureSuccessStatusCode();
        var file = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = file.GetProperty("id").GetString();
        Assert.Equal(fileName, file.GetProperty("filename").GetString());

        Assert.Equal(expected, await _client.GetStringAsync($"/v1/files/{fileId}/content"));
        var metadata = await _client.GetFromJsonAsync<JsonElement>($"/v1/files/{fileId}");
        Assert.Equal(fileName, metadata.GetProperty("filename").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount(expected), metadata.GetProperty("bytes").GetInt64());
    }

    [Fact]
    public async Task Chat_returns_only_the_last_completed_agent_message()
    {
        using var response = await SendMultiMessageChatAsync(stream: false);
        response.EnsureSuccessStatusCode();
        var completion = await response.Content.ReadFromJsonAsync<JsonElement>();
        var text = completion.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        Assert.Equal("Final response", text);

        using var streamedResponse = await SendMultiMessageChatAsync(stream: true);
        streamedResponse.EnsureSuccessStatusCode();
        var stream = await streamedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Final response", stream, StringComparison.Ordinal);
        Assert.DoesNotContain("Intermediate response", stream, StringComparison.Ordinal);
        Assert.EndsWith("data: [DONE]\n\n", stream, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        for (var attempt = 0; attempt < 20 && Directory.Exists(_factory.RootPath); attempt++)
        {
            try
            {
                Directory.Delete(_factory.RootPath, true);
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }
    }

    private Task<HttpResponseMessage> SendMultiMessageChatAsync(bool stream) =>
        _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            stream,
            messages = new[] { new { role = "user", content = "[scenario:multi-message]" } }
        });
}
