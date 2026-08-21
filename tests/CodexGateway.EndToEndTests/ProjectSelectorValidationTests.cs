using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests;

public sealed class ProjectSelectorValidationTests : IDisposable
{
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;

    public ProjectSelectorValidationTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [Theory]
    [InlineData("scope-one,scope-two")]
    [InlineData("scope/one")]
    [InlineData("-")]
    public async Task Malformed_or_comma_combined_project_header_returns_a_shaped_invalid_project_error(
        string projectSelector)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
        Assert.True(request.Headers.TryAddWithoutValidation("OpenAI-Project", projectSelector));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error");
        Assert.Equal("invalid_project", error.GetProperty("code").GetString());
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.Equal("OpenAI-Project", error.GetProperty("param").GetString());
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Services.GetRequiredService<CodexAppServerClient>().Dispose();
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
}
