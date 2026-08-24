using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using CodexGateway.Logic.Codex;
using CodexGateway.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Projects;

public sealed class ProjectGrantRevocationTests : IAsyncDisposable
{
    private readonly GatewayFactory _factory = new(maxConcurrent: 2, maxQueued: 2);
    private readonly HttpClient _client;

    public ProjectGrantRevocationTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Fact]
    public async Task File_request_queued_behind_a_run_rechecks_a_revoked_project_grant()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await CreateProjectAsync("revoke-files", true, grantDefault: true, deadline.Token);

        var holder = SendChatAsync(
            "revoke-files",
            "[scenario:gate:revoke-files-holder]",
            deadline.Token);
        await WaitForGateAsync("revoke-files-holder", deadline.Token);

        var revoke = UpdateProjectAsync("revoke-files", true, grantDefault: false, deadline.Token);
        await Task.Delay(100, deadline.Token);
        Assert.False(revoke.IsCompleted);

        var queuedFileList = SendProjectGetAsync("revoke-files", "/v1/files", deadline.Token);
        await Task.Delay(100, deadline.Token);
        Assert.False(queuedFileList.IsCompleted);

        await ReleaseGateAsync("revoke-files-holder", deadline.Token);
        using (var holderResponse = await holder)
        {
            holderResponse.EnsureSuccessStatusCode();
        }

        await revoke;
        using var denied = await queuedFileList;
        await AssertInvalidApiKeyAsync(denied, deadline.Token);
    }

    [Fact]
    public async Task Chat_queued_behind_a_run_rechecks_a_disabled_project_as_an_authentication_failure()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await CreateProjectAsync("disable-chat", true, grantDefault: true, deadline.Token);

        var holder = SendChatAsync(
            "disable-chat",
            "[scenario:gate:disable-chat-holder]",
            deadline.Token);
        await WaitForGateAsync("disable-chat-holder", deadline.Token);

        var disable = UpdateProjectAsync("disable-chat", false, grantDefault: true, deadline.Token);
        await Task.Delay(100, deadline.Token);
        Assert.False(disable.IsCompleted);

        var queued = SendChatAsync("disable-chat", "queued before project disable", deadline.Token);
        var coordinator = _factory.Services.GetRequiredService<RunCoordinator>();
        await WaitUntilAsync(() => coordinator.AdmittedRuns == 2 && coordinator.ActiveRuns == 1, deadline.Token);
        Assert.False(queued.IsCompleted);

        await ReleaseGateAsync("disable-chat-holder", deadline.Token);
        using (var holderResponse = await holder)
        {
            holderResponse.EnsureSuccessStatusCode();
        }

        await disable;
        using var denied = await queued;
        await AssertInvalidApiKeyAsync(denied, deadline.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        for (var attempt = 0; attempt < 20 && Directory.Exists(_factory.RootPath); attempt++)
        {
            try
            {
                Directory.Delete(_factory.RootPath, true);
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50);
            }
        }
    }

    private async Task CreateProjectAsync(
        string id,
        bool enabled,
        bool grantDefault,
        CancellationToken cancellationToken)
        => await _factory.CreateProjectAsync(
            id,
            id,
            enabled,
            grantDefault ? [new ProjectApiKeyAccess { ApiKeyId = "default" }] : [],
            cancellationToken);

    private async Task UpdateProjectAsync(
        string id,
        bool enabled,
        bool grantDefault,
        CancellationToken cancellationToken)
        => await _factory.UpdateProjectAsync(
            new ProjectDefinition
            {
                Id = id,
                Name = id,
                Enabled = enabled,
                ApiKeyAccess = grantDefault
                    ? [new ProjectApiKeyAccess { ApiKeyId = "default" }]
                    : []
            },
            cancellationToken);

    private Task<HttpResponseMessage> SendChatAsync(
        string projectId,
        string prompt,
        CancellationToken cancellationToken) =>
        SendProjectAsync(
            projectId,
            HttpMethod.Post,
            "/v1/chat/completions",
            JsonContent.Create(new
            {
                model = "gpt-test-sol",
                messages = new[] { new { role = "user", content = prompt } }
            }),
            cancellationToken);

    private Task<HttpResponseMessage> SendProjectGetAsync(
        string projectId,
        string path,
        CancellationToken cancellationToken) =>
        SendProjectAsync(projectId, HttpMethod.Get, path, null, cancellationToken);

    private async Task<HttpResponseMessage> SendProjectAsync(
        string projectId,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"/p/{projectId}{path}") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
        return await _client.SendAsync(request, cancellationToken);
    }

    private async Task WaitForGateAsync(string gateName, CancellationToken cancellationToken)
    {
        var path = GatePath(gateName, "started.json");
        await WaitUntilAsync(() => File.Exists(path), cancellationToken);
    }

    private Task ReleaseGateAsync(string gateName, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(GatePath(gateName, "release"), string.Empty, cancellationToken);

    private string GatePath(string gateName, string suffix) =>
        Path.Combine(_factory.ScenarioPath, "gates", gateName + "." + suffix);

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task AssertInvalidApiKeyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var error = json.GetProperty("error");
        Assert.Equal("invalid_api_key", error.GetProperty("code").GetString());
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
    }
}
