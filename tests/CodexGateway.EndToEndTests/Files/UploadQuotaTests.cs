using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Files;

public sealed class UploadQuotaTests : IDisposable
{
    private readonly GatewayFactory _factory = new(
        maxArtifactFileMegabytes: 2,
        maxArtifactTotalMegabytes: 1);
    private readonly HttpClient _client;

    public UploadQuotaTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
    }

    [Fact]
    public async Task Project_uploads_cannot_exceed_the_projects_aggregate_artifact_quota()
    {
        await _factory.CreateProjectAsync("upload-quota", "Upload quota");

        using var first = await UploadAsync("first.txt", 600 * 1024);
        first.EnsureSuccessStatusCode();

        using var rejected = await UploadAsync("second.txt", 600 * 1024);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("artifact_limit_exceeded", error.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(_factory.RootPath, error.ToString(), StringComparison.OrdinalIgnoreCase);
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

    private Task<HttpResponseMessage> UploadAsync(string name, int bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("assistants"), "purpose");
        form.Add(new ByteArrayContent(new byte[bytes]), "file", name);
        return SendAndDisposeAsync(form);
    }

    private async Task<HttpResponseMessage> SendAndDisposeAsync(MultipartFormDataContent form)
    {
        using (form)
        {
            return await _client.PostAsync("/p/upload-quota/v1/files", form);
        }
    }
}
